//----------------------------------------------------------------------------------
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//
// This sample is not supported under any Microsoft standard support program or
// service. It is provided to you solely for the purpose of illustration and is
// intended to be modified, tested, and validated by the customer prior to any
// production use. The entire risk arising out of the use or performance of this
// code remains with the customer.
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//----------------------------------------------------------------------------------

using System.Diagnostics;
using FaceLab.Core.Data;

namespace FaceLab.Core;

public interface IFaceRunner
{
    Task<RunRecord> RunAsync(ImageRecord image, FaceLabOptions options, CancellationToken cancellationToken);

    Task<CallTrace> TestConnectionAsync(FaceLabOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the same Detect -> Identify -> enroll pipeline as the Function App's ProcessPhotoFunction,
/// but against whatever configuration is currently under test, and persists the outcome (plus every
/// raw Face API call) to the local database.
/// </summary>
public sealed class FaceRunner : IFaceRunner
{
    private readonly IFaceApiClientFactory _clientFactory;
    private readonly IFaceLabRepository _repository;

    public FaceRunner(IFaceApiClientFactory clientFactory, IFaceLabRepository repository)
    {
        _clientFactory = clientFactory;
        _repository = repository;
    }

    public async Task<RunRecord> RunAsync(ImageRecord image, FaceLabOptions options, CancellationToken cancellationToken)
    {
        var validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException("Configuration is invalid: " + string.Join(" ", validationErrors));
        }

        if (image.ByteCount > options.MaxImageSizeBytes)
        {
            throw new InvalidOperationException(
                $"{image.FileName} is {image.ByteCount:N0} bytes, which exceeds the configured limit of {options.MaxImageSizeBytes:N0} bytes.");
        }

        var client = _clientFactory.Create(options);
        var run = new RunRecord
        {
            ImageId = image.Id,
            ConfigSnapshotJson = options.ToSnapshotJson(),
            StartedUtc = DateTimeOffset.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();
        var sightings = new List<(string PersonId, double Confidence)>();

        try
        {
            if (options.EnsureDynamicPersonGroupExists)
            {
                await client.EnsureDynamicPersonGroupExistsAsync(cancellationToken);
            }

            if (options.AutoEnrollUnmatchedFaces)
            {
                await DeleteExpiredProvisionalIdentitiesAsync(client, cancellationToken);
            }

            var detectedFaces = await client.DetectFacesAsync(image.Bytes, cancellationToken);
            run.DetectedFaceCount = detectedFaces.Count;

            if (detectedFaces.Count == 0)
            {
                run.Status = RunStatuses.NoFaces;
            }
            else
            {
                var faceIds = detectedFaces.Where(f => f.FaceId is not null).Select(f => f.FaceId!).ToList();
                var identifyResults = await client.IdentifyAsync(faceIds, cancellationToken);
                var identifiedByFaceId = identifyResults
                    .Where(r => r.FaceId is not null)
                    .ToDictionary(r => r.FaceId!, r => r);

                foreach (var face in detectedFaces)
                {
                    var result = await ProcessFaceAsync(client, options, image, face, identifiedByFaceId, cancellationToken);
                    run.Faces.Add(result);

                    var personId = result.MatchedPersonId ?? result.EnrolledPersonId;
                    if (personId is not null)
                    {
                        sightings.Add((personId, result.Confidence));
                    }
                }

                run.Status = run.Faces.Any(f => f.Outcome == FaceOutcomes.Failed)
                    ? RunStatuses.Failed
                    : RunStatuses.Completed;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = RunStatuses.Failed;
            run.ErrorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            run.DurationMs = stopwatch.ElapsedMilliseconds;
            run.Traces.AddRange(client.Traces.Select(CallTraceRecord.FromTrace));
        }

        var saved = await _repository.SaveRunAsync(run, cancellationToken);

        foreach (var (personId, confidence) in sightings)
        {
            await _repository.RecordSightingAsync(
                personId,
                options.DynamicPersonGroupId,
                confidence,
                run.StartedUtc,
                displayName: null,
                cancellationToken);
        }

        return saved;
    }

    public async Task<CallTrace> TestConnectionAsync(FaceLabOptions options, CancellationToken cancellationToken)
    {
        var validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException("Configuration is invalid: " + string.Join(" ", validationErrors));
        }

        var client = _clientFactory.Create(options);
        try
        {
            await client.EnsureDynamicPersonGroupExistsAsync(cancellationToken);
        }
        catch (FaceApiException) when (client.Traces.Count > 0)
        {
            // The trace carries the failure detail the user needs to see.
        }

        return client.Traces.LastOrDefault()
            ?? throw new FaceApiException("No Face API call was made while testing the connection.");
    }

    private async Task<FaceResultRecord> ProcessFaceAsync(
        IFaceApiClient client,
        FaceLabOptions options,
        ImageRecord image,
        DetectedFace face,
        IReadOnlyDictionary<string, IdentifyResult> identifiedByFaceId,
        CancellationToken cancellationToken)
    {
        var record = new FaceResultRecord
        {
            FaceId = face.FaceId,
            Top = face.FaceRectangle?.Top ?? 0,
            Left = face.FaceRectangle?.Left ?? 0,
            Width = face.FaceRectangle?.Width ?? 0,
            Height = face.FaceRectangle?.Height ?? 0,
            DebugInfoJson = FaceDebugInfo.FromDetectedFace(face)?.ToJson()
        };

        var candidates = face.FaceId is not null && identifiedByFaceId.TryGetValue(face.FaceId, out var identifyResult)
            ? identifyResult.Candidates.OrderByDescending(c => c.Confidence).ToList()
            : new List<IdentifyCandidate>();
        var candidate = candidates.FirstOrDefault();
        var runnerUp = candidates.Skip(1).FirstOrDefault();

        if (candidate?.PersonId is not null &&
            candidate.Confidence >= options.ConfidenceThreshold &&
            HasRequiredMargin(candidate.Confidence, runnerUp?.Confidence, options.CandidateConfidenceMargin))
        {
            record.Outcome = FaceOutcomes.Matched;
            record.MatchedPersonId = candidate.PersonId;
            record.Confidence = candidate.Confidence;

            try
            {
                await TryImproveManagedIdentityAsync(
                    client,
                    options,
                    image,
                    face,
                    candidate,
                    cancellationToken);
            }
            catch (FaceApiException ex)
            {
                record.DecisionReason = $"Matched, but automatic template improvement failed: {ex.Message}";
            }
            return record;
        }

        if (candidate?.PersonId is not null &&
            candidate.Confidence >= options.ConfidenceThreshold &&
            !HasRequiredMargin(candidate.Confidence, runnerUp?.Confidence, options.CandidateConfidenceMargin))
        {
            record.Outcome = FaceOutcomes.Ambiguous;
            record.Confidence = candidate.Confidence;
            record.DecisionReason =
                $"The leading active candidate did not exceed the runner-up by the required " +
                $"{options.CandidateConfidenceMargin:F2} margin.";
            return record;
        }

        if (!options.AutoEnrollUnmatchedFaces)
        {
            record.Outcome = FaceOutcomes.NoMatch;
            return record;
        }

        var quality = FaceEnrollmentQuality.Evaluate(face, image.Bytes, options);
        if (!quality.IsEligible)
        {
            record.Outcome = FaceOutcomes.Deferred;
            record.DecisionReason = quality.Reason;
            return record;
        }

        if (face.FaceId is null || face.FaceRectangle is null)
        {
            record.Outcome = FaceOutcomes.Failed;
            record.ErrorMessage = $"Face API did not return a rectangle for face {face.FaceId}, so it cannot be enrolled.";
            return record;
        }

        try
        {
            var provisionalMatches = new List<(ManagedIdentityRecord Identity, double Confidence)>();
            var provisionalIdentities = await _repository.GetUnexpiredProvisionalIdentitiesAsync(
                DateTimeOffset.UtcNow,
                cancellationToken);
            foreach (var provisional in provisionalIdentities)
            {
                var verification = await client.VerifyPersonAsync(face.FaceId, provisional.PersonId, cancellationToken);
                provisionalMatches.Add((provisional, verification.Confidence));
            }

            var rankedMatches = provisionalMatches
                .OrderByDescending(match => match.Confidence)
                .ToList();
            var best = rankedMatches.FirstOrDefault();
            var second = rankedMatches.Skip(1).FirstOrDefault();

            if (best.Identity is not null &&
                best.Confidence >= options.ProvisionalVerificationThreshold)
            {
                if (!HasRequiredMargin(
                        best.Confidence,
                        second.Identity is null ? null : second.Confidence,
                        options.CandidateConfidenceMargin))
                {
                    record.Outcome = FaceOutcomes.Ambiguous;
                    record.Confidence = best.Confidence;
                    record.DecisionReason =
                        $"The leading provisional identity did not exceed the runner-up by the required " +
                        $"{options.CandidateConfidenceMargin:F2} margin.";
                    return record;
                }

                record.ProvisionalPersonId = best.Identity.PersonId;
                record.Confidence = best.Confidence;
                if (await _repository.HasEnrollmentEvidenceAsync(
                        best.Identity.PersonId,
                        image.Sha256,
                        cancellationToken))
                {
                    if (best.Identity.EvidenceCount >= options.RequiredEnrollmentImages)
                    {
                        await PromoteAsync(client, best.Identity.PersonId, cancellationToken);
                        record.Outcome = FaceOutcomes.Promoted;
                        record.EnrolledPersonId = best.Identity.PersonId;
                        record.DecisionReason =
                            $"Automatically promoted after {best.Identity.EvidenceCount} distinct qualifying images.";
                        return record;
                    }

                    record.Outcome = FaceOutcomes.ProvisionalConfirmed;
                    record.DecisionReason =
                        "This image was already recorded for the provisional identity, so it did not count as independent evidence.";
                    return record;
                }

                await client.AddPersonFaceAsync(best.Identity.PersonId, image.Bytes, face.FaceRectangle, cancellationToken);
                await _repository.TryAddEnrollmentEvidenceAsync(
                    best.Identity.PersonId,
                    CreateEvidence(image, best.Confidence),
                    DateTimeOffset.UtcNow,
                    best.Confidence,
                    cancellationToken);

                if (best.Identity.EvidenceCount + 1 >= options.RequiredEnrollmentImages)
                {
                    await PromoteAsync(client, best.Identity.PersonId, cancellationToken);
                    record.Outcome = FaceOutcomes.Promoted;
                    record.EnrolledPersonId = best.Identity.PersonId;
                    record.DecisionReason =
                        $"Automatically promoted after {best.Identity.EvidenceCount + 1} distinct qualifying images.";
                }
                else
                {
                    record.Outcome = FaceOutcomes.ProvisionalConfirmed;
                    record.DecisionReason =
                        $"Recorded qualifying image {best.Identity.EvidenceCount + 1} of {options.RequiredEnrollmentImages}.";
                }

                return record;
            }

            var personId = await client.CreatePersonAsync($"person-{Guid.NewGuid():N}", cancellationToken);
            await client.AddPersonFaceAsync(personId, image.Bytes, face.FaceRectangle, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            await _repository.CreateProvisionalIdentityAsync(
                new ManagedIdentityRecord
                {
                    PersonId = personId,
                    DynamicPersonGroupId = options.DynamicPersonGroupId,
                    CreatedUtc = now,
                    LastSeenUtc = now,
                    ExpiresUtc = now.AddDays(options.ProvisionalExpirationDays),
                    LastConfidence = 0
                },
                CreateEvidence(image, 0),
                cancellationToken);

            record.Outcome = FaceOutcomes.ProvisionalCreated;
            record.ProvisionalPersonId = personId;
            record.DecisionReason =
                $"Created outside the dynamic group; {options.RequiredEnrollmentImages - 1} additional distinct qualifying image(s) required.";
        }
        catch (FaceApiException ex)
        {
            record.Outcome = FaceOutcomes.Failed;
            record.ErrorMessage = ex.Message;
        }

        return record;
    }

    private async Task DeleteExpiredProvisionalIdentitiesAsync(
        IFaceApiClient client,
        CancellationToken cancellationToken)
    {
        var expired = await _repository.GetExpiredProvisionalIdentitiesAsync(DateTimeOffset.UtcNow, cancellationToken);
        foreach (var identity in expired)
        {
            await client.DeletePersonAsync(identity.PersonId, cancellationToken);
            await _repository.DeleteManagedIdentityAsync(identity.PersonId, cancellationToken);
        }
    }

    private async Task TryImproveManagedIdentityAsync(
        IFaceApiClient client,
        FaceLabOptions options,
        ImageRecord image,
        DetectedFace face,
        IdentifyCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.Confidence < options.TemplateLearningThreshold ||
            !options.AutoEnrollUnmatchedFaces ||
            face.FaceRectangle is null ||
            !FaceEnrollmentQuality.Evaluate(face, image.Bytes, options).IsEligible)
        {
            return;
        }

        var managed = await _repository.GetManagedIdentityAsync(candidate.PersonId!, cancellationToken);
        if (managed?.State != ManagedIdentityStates.Active ||
            managed.EvidenceCount >= options.MaximumManagedFaceTemplates ||
            await _repository.HasEnrollmentEvidenceAsync(managed.PersonId, image.Sha256, cancellationToken))
        {
            return;
        }

        await client.AddPersonFaceAsync(managed.PersonId, image.Bytes, face.FaceRectangle, cancellationToken);
        await _repository.TryAddEnrollmentEvidenceAsync(
            managed.PersonId,
            CreateEvidence(image, candidate.Confidence),
            DateTimeOffset.UtcNow,
            candidate.Confidence,
            cancellationToken);
    }

    private static EnrollmentEvidenceRecord CreateEvidence(ImageRecord image, double confidence) => new()
    {
        ImageId = image.Id,
        ImageSha256 = image.Sha256,
        CapturedUtc = DateTimeOffset.UtcNow,
        Confidence = confidence
    };

    private async Task PromoteAsync(
        IFaceApiClient client,
        string personId,
        CancellationToken cancellationToken)
    {
        await client.AddPersonToDynamicGroupAsync(personId, cancellationToken);
        await _repository.PromoteManagedIdentityAsync(personId, cancellationToken);
    }

    private static bool HasRequiredMargin(double leading, double? runnerUp, double requiredMargin) =>
        runnerUp is null || leading - runnerUp.Value >= requiredMargin;
}
