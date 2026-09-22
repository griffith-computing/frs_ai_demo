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

namespace FaceLab.Core.Tests;

/// <summary>Records every outgoing request and replays a scripted queue of responses.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler Enqueue(HttpStatusCode statusCode, string json, string? operationLocation = null)
    {
        _responders.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            if (operationLocation is not null)
            {
                response.Headers.TryAddWithoutValidation("Operation-Location", operationLocation);
            }

            return response;
        });

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var keys) ? keys.First() : null,
            request.Headers.Authorization?.ToString(),
            body));

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response left for {request.Method} {request.RequestUri}.");
        }

        return _responders.Dequeue()(request);
    }
}

public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? SubscriptionKey,
    string? Authorization,
    string? Body);

public sealed class StubTokenCredential : TokenCredential
{
    public int CallCount { get; private set; }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        CallCount++;
        return new AccessToken("stub-token", DateTimeOffset.UtcNow.AddHours(1));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}

public static class TestOptions
{
    public static FaceLabOptions Create(Action<FaceLabOptions>? configure = null)
    {
        var options = new FaceLabOptions
        {
            Endpoint = "https://face.example.com",
            SubscriptionKey = "test-key",
            OperationPollIntervalMilliseconds = 1,
            OperationTimeoutSeconds = 1
        };

        configure?.Invoke(options);
        return options;
    }
}
