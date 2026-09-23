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

using FaceLab.Core.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceLab.Core.Tests;

public sealed class FakeFaceApiClient : IFaceApiClient, IFaceApiClientFactory
{
    private readonly List<CallTrace> _traces = new();
    private int _personCounter;

    public FakeFaceApiClient(FaceLabOptions options)
    {
        Options = options;
    }

    public FaceLabOptions Options { get; private set; }

    public IReadOnlyList<CallTrace> Traces => _traces;

    public List<DetectedFace> DetectedFaces { get; } = new();

    public List<IdentifyResult> IdentifyResults { get; } = new();

    public bool GroupEnsured { get; private set; }

    public List<string> CreatedPersonIds { get; } = new();

    public List<string> PersonsAddedToGroup { get; } = new();

    public List<string> FacesAddedToPeople { get; } = new();

    public Dictionary<string, VerifyResult> VerificationResults { get; } = new();

    public List<string> DeletedPersonIds { get; } = new();

    public Exception? EnsureGroupException { get; set; }

    public Exception? DetectException { get; set; }

    public Exception? CreatePersonException { get; set; }

    public IFaceApiClient Create(FaceLabOptions options)
    {
        Options = options;
        return this;
    }

    public Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(byte[] photo, CancellationToken cancellationToken)
    {
        Trace("detect faces");
        if (DetectException is not null)
        {
            throw DetectException;
        }

        return Task.FromResult<IReadOnlyList<DetectedFace>>(DetectedFaces);
    }

    public Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(IEnumerable<string> faceIds, CancellationToken cancellationToken)
    {
        Trace("identify faces");
        return Task.FromResult<IReadOnlyList<IdentifyResult>>(IdentifyResults);
    }

    public Task<VerifyResult> VerifyPersonAsync(string faceId, string personId, CancellationToken cancellationToken)
    {
        Trace("verify face against person");
        return Task.FromResult(
            VerificationResults.GetValueOrDefault(personId) ??
            new VerifyResult { IsIdentical = false, Confidence = 0 });
    }

    public Task EnsureDynamicPersonGroupExistsAsync(CancellationToken cancellationToken)
    {
        Trace("get dynamic person group");
        if (EnsureGroupException is not null)
        {
            throw EnsureGroupException;
        }

        GroupEnsured = true;
        return Task.CompletedTask;
    }

    public Task<string> CreatePersonAsync(string name, CancellationToken cancellationToken)
    {
        Trace("create person");
        if (CreatePersonException is not null)
        {
            throw CreatePersonException;
        }

        var personId = $"new-person-{++_personCounter}";
        CreatedPersonIds.Add(personId);
        return Task.FromResult(personId);
    }

    public Task AddPersonFaceAsync(string personId, byte[] photo, FaceRectangle targetFace, CancellationToken cancellationToken)
    {
        Trace("add person face");
        FacesAddedToPeople.Add(personId);
        return Task.CompletedTask;
    }

    public Task AddPersonToDynamicGroupAsync(string personId, CancellationToken cancellationToken)
    {
        Trace("add person to dynamic person group");
        PersonsAddedToGroup.Add(personId);
        return Task.CompletedTask;
    }

    public Task DeletePersonAsync(string personId, CancellationToken cancellationToken)
    {
        Trace("delete person");
        DeletedPersonIds.Add(personId);
        return Task.CompletedTask;
    }

    private void Trace(string operation) =>
        _traces.Add(new CallTrace
        {
            Operation = operation,
            Method = "POST",
            Url = Options.BaseAddress + operation,
            StatusCode = 200
        });
}

public sealed class FaceRunnerTests : IDisposable
{
    private readonly TempSqliteDatabase _database = new();
    private readonly FaceLabRepository _repository;

    public FaceRunnerTests()
    {
        _repository = new FaceLabRepository(_database);
        _repository.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose() => _database.Dispose();

    private static DetectedFace Face(string faceId) => new()
    {
        FaceId = faceId,
        FaceRectangle = new FaceRectangle { Top = 20, Left = 20, Width = 120, Height = 120 },
        FaceAttributes = new FaceAttributes
        {
            QualityForRecognition = "high",
            Mask = new FaceMask { NoseAndMouthCovered = false },
            HeadPose = new HeadPose()
        }
    };

    private async Task<ImageRecord> ImportAsync(int? byteCount = null, byte marker = 0) =>
        await _repository.SaveImageAsync(
            "face.jpg",
            "image/jpeg",
            byteCount is null
                ? CreateImageBytes(marker)
                : Enumerable.Range(1, byteCount.Value).Select(i => (byte)i).ToArray(),
            CancellationToken.None);

    private static byte[] CreateImageBytes(byte marker)
    {
        using var image = new Image<Rgba32>(300, 300, new Rgba32(marker, marker, marker));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task RunAsync_records_a_match_without_enrolling()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options);
        client.DetectedFaces.Add(Face("face-1"));
        client.IdentifyResults.Add(new IdentifyResult
        {
            FaceId = "face-1",
            Candidates = { new IdentifyCandidate { PersonId = "person-7", Confidence = 0.93 } }
        });

        var run = await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        Assert.Equal(RunStatuses.Completed, run.Status);
        Assert.Equal(1, run.DetectedFaceCount);
        var face = Assert.Single(run.Faces);
        Assert.Equal(FaceOutcomes.Matched, face.Outcome);
        Assert.Equal("person-7", face.MatchedPersonId);
        Assert.Equal(0.93, face.Confidence);
        Assert.Empty(client.CreatedPersonIds);

        var person = Assert.Single(await _repository.GetPeopleAsync(CancellationToken.None));
        Assert.Equal("person-7", person.PersonId);
        Assert.Equal(1, person.SightingCount);
    }

    [Fact]
    public async Task RunAsync_creates_a_provisional_identity_when_auto_enroll_is_on()
    {
        var options = TestOptions.Create(o => o.AutoEnrollUnmatchedFaces = true);
        var client = new FakeFaceApiClient(options);
        client.DetectedFaces.Add(Face("face-1"));

        var run = await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        var face = Assert.Single(run.Faces);
        Assert.Equal(FaceOutcomes.ProvisionalCreated, face.Outcome);
        Assert.Equal(Assert.Single(client.CreatedPersonIds), face.ProvisionalPersonId);
        Assert.Empty(client.PersonsAddedToGroup);
        Assert.True(client.GroupEnsured);
    }

    [Fact]
    public async Task RunAsync_leaves_the_directory_untouched_when_auto_enroll_is_off()
    {
        var options = TestOptions.Create(o => o.AutoEnrollUnmatchedFaces = false);
        var client = new FakeFaceApiClient(options);
        client.DetectedFaces.Add(Face("face-1"));

        var run = await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        Assert.Equal(FaceOutcomes.NoMatch, Assert.Single(run.Faces).Outcome);
        Assert.Empty(client.CreatedPersonIds);
        Assert.Empty(await _repository.GetPeopleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_skips_the_group_check_when_the_option_is_disabled()
    {
        var options = TestOptions.Create(o =>
        {
            o.EnsureDynamicPersonGroupExists = false;
            o.AutoEnrollUnmatchedFaces = false;
        });
        var client = new FakeFaceApiClient(options);

        await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        Assert.False(client.GroupEnsured);
    }

    [Fact]
    public async Task RunAsync_reports_no_faces_and_skips_identify()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options);

        var run = await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        Assert.Equal(RunStatuses.NoFaces, run.Status);
        Assert.Empty(run.Faces);
        Assert.DoesNotContain(client.Traces, t => t.Operation == "identify faces");
    }

    [Fact]
    public async Task RunAsync_marks_a_face_failed_when_enrollment_throws()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options) { CreatePersonException = new FaceApiException("person quota exceeded") };
        client.DetectedFaces.Add(Face("face-1"));

        var run = await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        Assert.Equal(RunStatuses.Failed, run.Status);
        var face = Assert.Single(run.Faces);
        Assert.Equal(FaceOutcomes.Failed, face.Outcome);
        Assert.Contains("quota", face.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_defers_a_face_that_fails_the_quality_gate()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options);
        client.DetectedFaces.Add(new DetectedFace
        {
            FaceId = "face-1",
            FaceRectangle = new FaceRectangle { Left = 20, Top = 20, Width = 120, Height = 120 },
            FaceAttributes = new FaceAttributes
            {
                QualityForRecognition = "medium",
                Mask = new FaceMask { NoseAndMouthCovered = false },
                HeadPose = new HeadPose()
            }
        });

        var run = await new FaceRunner(client, _repository).RunAsync(
            await ImportAsync(),
            options,
            CancellationToken.None);

        var face = Assert.Single(run.Faces);
        Assert.Equal(FaceOutcomes.Deferred, face.Outcome);
        Assert.Contains("quality", face.DecisionReason);
        Assert.Empty(client.CreatedPersonIds);
    }

    [Fact]
    public async Task RunAsync_promotes_a_provisional_after_a_second_distinct_qualifying_image()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options);
        client.DetectedFaces.Add(Face("face-1"));
        var runner = new FaceRunner(client, _repository);

        var firstRun = await runner.RunAsync(await ImportAsync(marker: 1), options, CancellationToken.None);
        var personId = Assert.Single(client.CreatedPersonIds);
        Assert.Equal(FaceOutcomes.ProvisionalCreated, Assert.Single(firstRun.Faces).Outcome);

        client.VerificationResults[personId] = new VerifyResult { IsIdentical = true, Confidence = 0.88 };
        var secondRun = await runner.RunAsync(await ImportAsync(marker: 2), options, CancellationToken.None);

        var face = Assert.Single(secondRun.Faces);
        Assert.Equal(FaceOutcomes.Promoted, face.Outcome);
        Assert.Equal(personId, face.EnrolledPersonId);
        Assert.Equal(personId, Assert.Single(client.PersonsAddedToGroup));
        var managed = await _repository.GetManagedIdentityAsync(personId, CancellationToken.None);
        Assert.Equal(ManagedIdentityStates.Active, managed!.State);
        Assert.Equal(2, managed.EvidenceCount);
        Assert.Equal(personId, Assert.Single(await _repository.GetPeopleAsync(CancellationToken.None)).PersonId);
    }

    [Fact]
    public async Task RunAsync_does_not_count_the_same_image_twice_for_promotion()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options);
        client.DetectedFaces.Add(Face("face-1"));
        var runner = new FaceRunner(client, _repository);
        var image = await ImportAsync(marker: 3);

        await runner.RunAsync(image, options, CancellationToken.None);
        var personId = Assert.Single(client.CreatedPersonIds);
        client.VerificationResults[personId] = new VerifyResult { IsIdentical = true, Confidence = 0.9 };

        var repeat = await runner.RunAsync(image, options, CancellationToken.None);

        Assert.Equal(FaceOutcomes.ProvisionalConfirmed, Assert.Single(repeat.Faces).Outcome);
        Assert.Empty(client.PersonsAddedToGroup);
        Assert.Equal(1, (await _repository.GetManagedIdentityAsync(personId, CancellationToken.None))!.EvidenceCount);
    }

    [Fact]
    public async Task RunAsync_rejects_ambiguous_active_candidates()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options);
        client.DetectedFaces.Add(Face("face-1"));
        client.IdentifyResults.Add(new IdentifyResult
        {
            FaceId = "face-1",
            Candidates =
            {
                new IdentifyCandidate { PersonId = "person-1", Confidence = 0.91 },
                new IdentifyCandidate { PersonId = "person-2", Confidence = 0.86 }
            }
        });

        var run = await new FaceRunner(client, _repository).RunAsync(
            await ImportAsync(),
            options,
            CancellationToken.None);

        var face = Assert.Single(run.Faces);
        Assert.Equal(FaceOutcomes.Ambiguous, face.Outcome);
        Assert.Empty(await _repository.GetPeopleAsync(CancellationToken.None));
        Assert.Empty(client.CreatedPersonIds);
    }

    [Fact]
    public async Task RunAsync_deletes_expired_provisional_people_before_processing()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options);
        await _repository.CreateProvisionalIdentityAsync(
            new ManagedIdentityRecord
            {
                PersonId = "expired-person",
                DynamicPersonGroupId = options.DynamicPersonGroupId,
                CreatedUtc = DateTimeOffset.UtcNow.AddDays(-31),
                LastSeenUtc = DateTimeOffset.UtcNow.AddDays(-31),
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new EnrollmentEvidenceRecord
            {
                ImageId = (await ImportAsync(marker: 4)).Id,
                ImageSha256 = (await ImportAsync(marker: 4)).Sha256
            },
            CancellationToken.None);

        await new FaceRunner(client, _repository).RunAsync(
            await ImportAsync(marker: 5),
            options,
            CancellationToken.None);

        Assert.Equal("expired-person", Assert.Single(client.DeletedPersonIds));
        Assert.Null(await _repository.GetManagedIdentityAsync("expired-person", CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_learns_only_from_a_high_confidence_match_for_a_managed_active_identity()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options);
        var firstImage = await ImportAsync(marker: 6);
        await _repository.CreateProvisionalIdentityAsync(
            new ManagedIdentityRecord
            {
                PersonId = "managed-person",
                DynamicPersonGroupId = options.DynamicPersonGroupId,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            },
            new EnrollmentEvidenceRecord
            {
                ImageId = firstImage.Id,
                ImageSha256 = firstImage.Sha256
            },
            CancellationToken.None);
        await _repository.PromoteManagedIdentityAsync("managed-person", CancellationToken.None);
        client.DetectedFaces.Add(Face("face-1"));
        client.IdentifyResults.Add(new IdentifyResult
        {
            FaceId = "face-1",
            Candidates = { new IdentifyCandidate { PersonId = "managed-person", Confidence = 0.95 } }
        });

        await new FaceRunner(client, _repository).RunAsync(
            await ImportAsync(marker: 7),
            options,
            CancellationToken.None);

        Assert.Equal("managed-person", Assert.Single(client.FacesAddedToPeople));
        Assert.Equal(2, (await _repository.GetManagedIdentityAsync("managed-person", CancellationToken.None))!.EvidenceCount);
    }

    [Fact]
    public async Task RunAsync_captures_a_pipeline_failure_on_the_run_and_still_persists_traces()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options) { DetectException = new FaceApiException("endpoint unreachable") };

        var run = await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        Assert.Equal(RunStatuses.Failed, run.Status);
        Assert.Contains("endpoint unreachable", run.ErrorMessage);

        var reloaded = await _repository.GetRunAsync(run.Id, CancellationToken.None);
        Assert.NotEmpty(reloaded!.Traces);
    }

    [Fact]
    public async Task RunAsync_stores_the_configuration_snapshot_used_for_the_run()
    {
        var options = TestOptions.Create(o => o.ConfidenceThreshold = 0.42);
        var client = new FakeFaceApiClient(options);

        var run = await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        var reloaded = await _repository.GetRunAsync(run.Id, CancellationToken.None);
        Assert.Equal(0.42, reloaded!.Config.ConfidenceThreshold);
    }

    [Fact]
    public async Task RunAsync_rejects_an_invalid_configuration_before_calling_the_api()
    {
        var options = TestOptions.Create(o => o.Endpoint = string.Empty);
        var client = new FakeFaceApiClient(options);
        var image = await ImportAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new FaceRunner(client, _repository).RunAsync(image, options, CancellationToken.None));

        Assert.Contains("Configuration is invalid", exception.Message);
        Assert.Empty(client.Traces);
    }

    [Fact]
    public async Task RunAsync_rejects_an_image_larger_than_the_configured_limit()
    {
        var options = TestOptions.Create(o => o.MaxImageSizeBytes = 2);
        var client = new FakeFaceApiClient(options);
        var image = await ImportAsync(10);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new FaceRunner(client, _repository).RunAsync(image, options, CancellationToken.None));

        Assert.Contains("exceeds the configured limit", exception.Message);
        Assert.Empty(client.Traces);
    }

    [Fact]
    public async Task RunAsync_captures_detect_debug_info_on_the_face_result()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options);
        client.DetectedFaces.Add(new DetectedFace
        {
            FaceId = "face-1",
            FaceRectangle = new FaceRectangle { Top = 20, Left = 20, Width = 120, Height = 120 },
            RecognitionModel = "recognition_04",
            FaceAttributes = new FaceAttributes
            {
                HeadPose = new HeadPose { Pitch = 1, Roll = 2, Yaw = 3 },
                Mask = new FaceMask { NoseAndMouthCovered = false },
                QualityForRecognition = "high"
            }
        });

        var run = await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        var face = Assert.Single(run.Faces);
        Assert.NotNull(face.DebugInfoJson);
        var debugInfo = FaceDebugInfo.FromJson(face.DebugInfoJson);
        Assert.Equal("recognition_04", debugInfo!.RecognitionModel);
        Assert.Equal(1, debugInfo.FaceAttributes!.HeadPose!.Pitch);
    }

    [Fact]
    public async Task TestConnectionAsync_returns_the_last_trace_even_when_the_call_fails()
    {
        var options = TestOptions.Create();
        var client = new FakeFaceApiClient(options) { EnsureGroupException = new FaceApiException("401 Unauthorized") };

        var trace = await new FaceRunner(client, _repository).TestConnectionAsync(options, CancellationToken.None);

        Assert.Equal("get dynamic person group", trace.Operation);
    }
}
