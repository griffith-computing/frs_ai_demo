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

    private static async Task<FaceResultRecord> ProcessFaceAsync(
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
            Height = face.FaceRectangle?.Height ?? 0
        };

        var candidate = face.FaceId is not null && identifiedByFaceId.TryGetValue(face.FaceId, out var identifyResult)
            ? identifyResult.Candidates.OrderByDescending(c => c.Confidence).FirstOrDefault()
            : null;

        if (candidate?.PersonId is not null)
        {
            record.Outcome = FaceOutcomes.Matched;
            record.MatchedPersonId = candidate.PersonId;
            record.Confidence = candidate.Confidence;
            return record;
        }

        if (!options.AutoEnrollUnmatchedFaces)
        {
            record.Outcome = FaceOutcomes.NoMatch;
            return record;
        }

        if (face.FaceRectangle is null)
        {
            record.Outcome = FaceOutcomes.Failed;
            record.ErrorMessage = $"Face API did not return a rectangle for face {face.FaceId}, so it cannot be enrolled.";
            return record;
        }

        try
        {
            var personId = await client.CreatePersonAsync($"person-{Guid.NewGuid():N}", cancellationToken);
            await client.AddPersonFaceAsync(personId, image.Bytes, face.FaceRectangle, cancellationToken);
            await client.AddPersonToDynamicGroupAsync(personId, cancellationToken);

            record.Outcome = FaceOutcomes.Enrolled;
            record.EnrolledPersonId = personId;
        }
        catch (FaceApiException ex)
        {
            record.Outcome = FaceOutcomes.Failed;
            record.ErrorMessage = ex.Message;
        }

        return record;
    }
}
