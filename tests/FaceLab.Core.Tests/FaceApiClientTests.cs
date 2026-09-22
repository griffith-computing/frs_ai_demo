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

using System.Net;
using System.Text.Json;

namespace FaceLab.Core.Tests;

public sealed class FaceApiClientTests
{
    private const string DetectResponse =
        """[{"faceId":"face-1","faceRectangle":{"top":10,"left":20,"width":30,"height":40}}]""";

    [Fact]
    public async Task DetectFacesAsync_uses_the_configured_models_and_api_version()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, DetectResponse);
        var options = TestOptions.Create(o =>
        {
            o.ApiVersionSegment = "face/v1.3";
            o.DetectionModel = "detection_01";
            o.RecognitionModel = "recognition_02";
        });
        var client = new FaceApiClient(new HttpClient(handler), options);

        var faces = await client.DetectFacesAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        var face = Assert.Single(faces);
        Assert.Equal("face-1", face.FaceId);
        Assert.Equal(20, face.FaceRectangle!.Left);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("face/v1.3/detect", request.Uri.ToString());
        Assert.Contains("detectionModel=detection_01", request.Uri.Query);
        Assert.Contains("recognitionModel=recognition_02", request.Uri.Query);
    }

    [Fact]
    public async Task DetectFacesAsync_sends_the_subscription_key_header_in_key_mode()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, DetectResponse);
        var options = TestOptions.Create(o => o.SubscriptionKey = "super-secret");
        var client = new FaceApiClient(new HttpClient(handler), options);

        await client.DetectFacesAsync(new byte[] { 1 }, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("super-secret", request.SubscriptionKey);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task DetectFacesAsync_sends_a_bearer_token_in_credential_mode()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, DetectResponse);
        var options = TestOptions.Create(o => o.AuthMode = FaceAuthMode.DefaultAzureCredential);
        var credential = new StubTokenCredential();
        var client = new FaceApiClient(new HttpClient(handler), options, credential);

        await client.DetectFacesAsync(new byte[] { 1 }, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer stub-token", request.Authorization);
        Assert.Null(request.SubscriptionKey);
        Assert.Equal(1, credential.CallCount);
    }

    [Fact]
    public void Constructor_requires_a_credential_in_credential_mode()
    {
        var options = TestOptions.Create(o => o.AuthMode = FaceAuthMode.DefaultAzureCredential);

        Assert.Throws<ArgumentNullException>(() =>
            new FaceApiClient(new HttpClient(new StubHttpMessageHandler()), options));
    }

    [Fact]
    public async Task IdentifyAsync_sends_the_configured_threshold_group_and_candidate_count()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """[{"faceId":"face-1","candidates":[{"personId":"person-1","confidence":0.92}]}]""");
        var options = TestOptions.Create(o =>
        {
            o.ConfidenceThreshold = 0.85;
            o.MaxCandidatesReturned = 3;
            o.DynamicPersonGroupId = "custom-group";
        });
        var client = new FaceApiClient(new HttpClient(handler), options);

        var results = await client.IdentifyAsync(new[] { "face-1" }, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("person-1", result.Candidates[0].PersonId);

        var payload = JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement;
        Assert.Equal(0.85, payload.GetProperty("confidenceThreshold").GetDouble());
        Assert.Equal(3, payload.GetProperty("maxNumOfCandidatesReturned").GetInt32());
        Assert.Equal("custom-group", payload.GetProperty("dynamicPersonGroupId").GetString());
    }

    [Fact]
    public async Task IdentifyAsync_batches_face_ids_using_the_configured_batch_size()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, "[]")
            .Enqueue(HttpStatusCode.OK, "[]");
        var options = TestOptions.Create(o => o.IdentifyBatchSize = 2);
        var client = new FaceApiClient(new HttpClient(handler), options);

        await client.IdentifyAsync(new[] { "a", "b", "c" }, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, JsonDocument.Parse(handler.Requests[0].Body!).RootElement.GetProperty("faceIds").GetArrayLength());
        Assert.Equal(1, JsonDocument.Parse(handler.Requests[1].Body!).RootElement.GetProperty("faceIds").GetArrayLength());
    }

    [Fact]
    public async Task IdentifyAsync_skips_the_call_when_there_are_no_face_ids()
    {
        var handler = new StubHttpMessageHandler();
        var client = new FaceApiClient(new HttpClient(handler), TestOptions.Create());

        var results = await client.IdentifyAsync(Array.Empty<string>(), CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EnsureDynamicPersonGroupExistsAsync_creates_the_group_when_it_is_missing()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":{"code":"ResourceNotFound"}}""")
            .Enqueue(HttpStatusCode.OK, "{}");
        var client = new FaceApiClient(new HttpClient(handler), TestOptions.Create());

        await client.EnsureDynamicPersonGroupExistsAsync(CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
    }

    [Fact]
    public async Task EnsureDynamicPersonGroupExistsAsync_does_not_recreate_an_existing_group()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{"dynamicPersonGroupId":"frs-ai-demo-group"}""");
        var client = new FaceApiClient(new HttpClient(handler), TestOptions.Create());

        await client.EnsureDynamicPersonGroupExistsAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EnsureDynamicPersonGroupExistsAsync_throws_on_a_non_404_failure()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized, """{"error":{"code":"Unauthorized"}}""");
        var client = new FaceApiClient(new HttpClient(handler), TestOptions.Create());

        var exception = await Assert.ThrowsAsync<FaceApiException>(
            () => client.EnsureDynamicPersonGroupExistsAsync(CancellationToken.None));

        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task CreatePersonAsync_polls_the_operation_until_it_succeeds()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"personId":"person-9"}""", "https://face.example.com/operations/1")
            .Enqueue(HttpStatusCode.OK, """{"status":"running"}""")
            .Enqueue(HttpStatusCode.OK, """{"status":"succeeded"}""");
        var client = new FaceApiClient(new HttpClient(handler), TestOptions.Create());

        var personId = await client.CreatePersonAsync("person-name", CancellationToken.None);

        Assert.Equal("person-9", personId);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task CreatePersonAsync_throws_when_the_operation_fails()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"personId":"person-9"}""", "https://face.example.com/operations/1")
            .Enqueue(HttpStatusCode.OK, """{"status":"failed","message":"quota exceeded"}""");
        var client = new FaceApiClient(new HttpClient(handler), TestOptions.Create());

        var exception = await Assert.ThrowsAsync<FaceApiException>(
            () => client.CreatePersonAsync("person-name", CancellationToken.None));

        Assert.Contains("quota exceeded", exception.Message);
    }

    [Fact]
    public async Task CreatePersonAsync_throws_when_the_operation_never_completes_within_the_timeout()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"personId":"person-9"}""", "https://face.example.com/operations/1");
        for (var i = 0; i < 200; i++)
        {
            handler.Enqueue(HttpStatusCode.OK, """{"status":"running"}""");
        }

        var options = TestOptions.Create(o =>
        {
            o.OperationTimeoutSeconds = 1;
            o.OperationPollIntervalMilliseconds = 100;
        });
        var client = new FaceApiClient(new HttpClient(handler), options);

        var exception = await Assert.ThrowsAsync<FaceApiException>(
            () => client.CreatePersonAsync("person-name", CancellationToken.None));

        Assert.Contains("Timed out", exception.Message);
    }

    [Fact]
    public async Task AddPersonFaceAsync_sends_the_target_rectangle_and_recognition_model()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, "{}", "https://face.example.com/operations/2")
            .Enqueue(HttpStatusCode.OK, """{"status":"succeeded"}""");
        var options = TestOptions.Create(o => o.RecognitionModel = "recognition_04");
        var client = new FaceApiClient(new HttpClient(handler), options);

        await client.AddPersonFaceAsync(
            "person-9",
            new byte[] { 9, 9 },
            new FaceRectangle { Left = 1, Top = 2, Width = 3, Height = 4 },
            CancellationToken.None);

        var uri = handler.Requests[0].Uri.ToString();
        Assert.Contains("persons/person-9/recognitionModels/recognition_04/persistedfaces", uri);
        Assert.Contains("targetFace=1,2,3,4", Uri.UnescapeDataString(uri));
    }

    [Fact]
    public async Task Every_call_is_captured_as_a_trace()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, DetectResponse)
            .Enqueue(HttpStatusCode.OK, "[]");
        var client = new FaceApiClient(new HttpClient(handler), TestOptions.Create());

        await client.DetectFacesAsync(new byte[] { 1 }, CancellationToken.None);
        await client.IdentifyAsync(new[] { "face-1" }, CancellationToken.None);

        Assert.Equal(2, client.Traces.Count);
        Assert.Equal("detect faces", client.Traces[0].Operation);
        Assert.Equal(200, client.Traces[0].StatusCode);
        Assert.True(client.Traces[0].IsSuccess);
        Assert.Equal("identify faces", client.Traces[1].Operation);
    }

    [Fact]
    public async Task A_failed_call_is_traced_with_its_status_and_error()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.TooManyRequests, """{"error":{"code":"429"}}""");
        var client = new FaceApiClient(new HttpClient(handler), TestOptions.Create());

        await Assert.ThrowsAsync<FaceApiException>(
            () => client.DetectFacesAsync(new byte[] { 1 }, CancellationToken.None));

        var trace = Assert.Single(client.Traces);
        Assert.Equal(429, trace.StatusCode);
        Assert.False(trace.IsSuccess);
        Assert.NotNull(trace.Error);
    }

    [Fact]
    public void Truncate_caps_long_response_bodies()
    {
        var body = new string('x', CallTrace.MaxBodyLength + 100);

        var truncated = CallTrace.Truncate(body)!;

        Assert.True(truncated.Length < body.Length + 50);
        Assert.Contains("truncated", truncated);
    }
}
