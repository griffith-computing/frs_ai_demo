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
using System.Text;
using Azure.Core;
using FrsAiDemo.FunctionApp.Models;
using FrsAiDemo.FunctionApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrsAiDemo.FunctionApp.Tests;

public sealed class FaceApiServiceTests
{
    [Fact]
    public async Task AddPersonFace_TargetsDetectedRectangleAndWaitsForEnrollment()
    {
        var handler = new RecordingHandler(
            _ => Response(
                HttpStatusCode.Accepted,
                "{}",
                operationLocation: "https://face.test/face/v1.2-preview.1/operations/operation-1"),
            _ => Response(HttpStatusCode.OK, """{"status":"succeeded"}"""));
        var service = CreateService(handler);

        await service.AddPersonFaceAsync(
            "person-1",
            new MemoryStream([1, 2, 3]),
            new FaceRectangle { Left = 10, Top = 20, Width = 30, Height = 40 },
            CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(
            "/face/v1.2-preview.1/persons/person-1/recognitionModels/recognition_04/persistedfaces" +
            "?targetFace=10,20,30,40&detectionModel=detection_03",
            handler.Requests[0].Uri.PathAndQuery);
        Assert.Equal("application/octet-stream", handler.Requests[0].ContentType);
        Assert.Equal("/face/v1.2-preview.1/operations/operation-1", handler.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task EnsureDynamicPersonGroupExists_CreatesOnlyWhenMissing()
    {
        var handler = new RecordingHandler(
            _ => Response(HttpStatusCode.NotFound, """{"error":{"code":"NotFound"}}"""),
            _ => Response(HttpStatusCode.OK, "{}"));
        var service = CreateService(handler);

        await service.EnsureDynamicPersonGroupExistsAsync(CancellationToken.None);

        Assert.Equal([HttpMethod.Get, HttpMethod.Put], handler.Requests.Select(request => request.Method));
        Assert.Contains("\"name\":\"test-group\"", handler.Requests[1].Body);
    }

    [Fact]
    public async Task EnsureDynamicPersonGroupExists_SurfacesUnexpectedLookupFailure()
    {
        var handler = new RecordingHandler(
            _ => Response(HttpStatusCode.Forbidden, """{"error":{"code":"Forbidden"}}"""));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureDynamicPersonGroupExistsAsync(CancellationToken.None));

        Assert.Contains("status 403", exception.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AddPersonToDynamicGroup_UsesPersonDirectoryMembershipContract()
    {
        var handler = new RecordingHandler(
            _ => Response(
                HttpStatusCode.Accepted,
                "{}",
                operationLocation: "https://face.test/face/v1.2-preview.1/operations/operation-2"),
            _ => Response(HttpStatusCode.OK, """{"status":"succeeded"}"""));
        var service = CreateService(handler);

        await service.AddPersonToDynamicGroupAsync("person-1", CancellationToken.None);

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("/face/v1.2-preview.1/dynamicpersongroups/test-group", request.Uri.AbsolutePath);
        Assert.Contains("\"addPersonIds\":[\"person-1\"]", request.Body);
        Assert.Equal("/face/v1.2-preview.1/operations/operation-2", handler.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Identify_BatchesMoreThanTenFaces()
    {
        var handler = new RecordingHandler(
            _ => Response(HttpStatusCode.OK, "[]"),
            _ => Response(HttpStatusCode.OK, "[]"));
        var service = CreateService(handler);

        await service.IdentifyAsync(
            Enumerable.Range(1, 11).Select(index => $"face-{index}"),
            CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("\"face-10\"", handler.Requests[0].Body);
        Assert.DoesNotContain("\"face-11\"", handler.Requests[0].Body);
        Assert.Contains("\"face-11\"", handler.Requests[1].Body);
    }

    private static FaceApiService CreateService(RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FaceApi:Endpoint"] = "https://face.test/",
                ["FaceApi:DynamicPersonGroupId"] = "test-group",
                ["FaceApi:OperationPollIntervalMilliseconds"] = "1",
                ["FaceApi:OperationTimeoutSeconds"] = "2"
            })
            .Build();
        return new FaceApiService(
            new HttpClient(handler),
            new StaticTokenCredential(),
            configuration,
            NullLogger<FaceApiService>.Instance);
    }

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string json,
        string? operationLocation = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (operationLocation is not null)
        {
            response.Headers.Add("Operation-Location", operationLocation);
        }
        return response;
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ContentType, string Body);

    private sealed class RecordingHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content?.Headers.ContentType?.MediaType,
                body));
            return _responses.Dequeue()(request);
        }
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("test-token", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) => ValueTask.FromResult(Token);
    }
}
