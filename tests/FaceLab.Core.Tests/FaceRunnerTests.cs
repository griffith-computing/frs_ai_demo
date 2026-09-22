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
        return Task.CompletedTask;
    }

    public Task AddPersonToDynamicGroupAsync(string personId, CancellationToken cancellationToken)
    {
        Trace("add person to dynamic person group");
        PersonsAddedToGroup.Add(personId);
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
        FaceRectangle = new FaceRectangle { Top = 1, Left = 2, Width = 3, Height = 4 }
    };

    private async Task<ImageRecord> ImportAsync(int byteCount = 4) =>
        await _repository.SaveImageAsync(
            "face.jpg",
            "image/jpeg",
            Enumerable.Range(1, byteCount).Select(i => (byte)i).ToArray(),
            CancellationToken.None);

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
    public async Task RunAsync_enrolls_an_unmatched_face_when_auto_enroll_is_on()
    {
        var options = TestOptions.Create(o => o.AutoEnrollUnmatchedFaces = true);
        var client = new FakeFaceApiClient(options);
        client.DetectedFaces.Add(Face("face-1"));

        var run = await new FaceRunner(client, _repository).RunAsync(await ImportAsync(), options, CancellationToken.None);

        var face = Assert.Single(run.Faces);
        Assert.Equal(FaceOutcomes.Enrolled, face.Outcome);
        Assert.Equal(Assert.Single(client.CreatedPersonIds), face.EnrolledPersonId);
        Assert.Equal(face.EnrolledPersonId, Assert.Single(client.PersonsAddedToGroup));
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
            FaceRectangle = new FaceRectangle { Top = 1, Left = 2, Width = 3, Height = 4 },
            RecognitionModel = "recognition_04",
            FaceAttributes = new FaceAttributes
            {
                HeadPose = new HeadPose { Pitch = 1, Roll = 2, Yaw = 3 }
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
