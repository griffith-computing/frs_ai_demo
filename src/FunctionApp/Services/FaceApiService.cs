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

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FrsAiDemo.FunctionApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrsAiDemo.FunctionApp.Services;

public interface IFaceApiService
{
    string DynamicPersonGroupId { get; }

    Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(Stream photo, CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(IEnumerable<string> faceIds, CancellationToken cancellationToken);
    Task EnsureDynamicPersonGroupExistsAsync(CancellationToken cancellationToken);
    Task<string> CreatePersonAsync(string name, CancellationToken cancellationToken);
    Task AddPersonFaceAsync(string personId, Stream photo, FaceRectangle targetFace, CancellationToken cancellationToken);
    Task AddPersonToDynamicGroupAsync(string personId, CancellationToken cancellationToken);
}

/// <summary>
/// REST wrapper around Azure AI Face Person Directory and Dynamic Person Groups. Person
/// Directory processes enrollment updates automatically, so no explicit training is required.
/// </summary>
public sealed class FaceApiService : IFaceApiService
{
    private static readonly string[] FaceApiScope = { "https://cognitiveservices.azure.com/.default" };
    private const string ApiVersionSegment = "face/v1.2-preview.1";
    private const string DetectionModel = "detection_03";
    private const string RecognitionModel = "recognition_04";

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _dynamicPersonGroupId;
    private readonly TimeSpan _operationTimeout;
    private readonly TimeSpan _operationPollInterval;
    private readonly ILogger<FaceApiService> _logger;

    public string DynamicPersonGroupId => _dynamicPersonGroupId;

    public FaceApiService(HttpClient httpClient, TokenCredential credential, IConfiguration configuration, ILogger<FaceApiService> logger)
    {
        _httpClient = httpClient;
        _credential = credential;
        _logger = logger;

        var endpoint = configuration["FaceApi:Endpoint"]
            ?? throw new InvalidOperationException("FaceApi:Endpoint configuration value is required.");
        _httpClient.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        _dynamicPersonGroupId = configuration["FaceApi:DynamicPersonGroupId"] ?? "frs-ai-demo-group";
        _operationTimeout = TimeSpan.FromSeconds(configuration.GetValue("FaceApi:OperationTimeoutSeconds", 30));
        _operationPollInterval = TimeSpan.FromMilliseconds(configuration.GetValue("FaceApi:OperationPollIntervalMilliseconds", 500));
    }

    public async Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(Stream photo, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            $"{ApiVersionSegment}/detect?returnFaceId=true&recognitionModel={RecognitionModel}&detectionModel={DetectionModel}",
            cancellationToken);
        request.Content = new StreamContent(photo) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "detect faces", cancellationToken);

        var faces = await response.Content.ReadFromJsonAsync<List<DetectedFace>>(cancellationToken: cancellationToken);
        return faces ?? new List<DetectedFace>();
    }

    public async Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(IEnumerable<string> faceIds, CancellationToken cancellationToken)
    {
        var faceIdList = faceIds.ToList();
        if (faceIdList.Count == 0)
        {
            return Array.Empty<IdentifyResult>();
        }

        var results = new List<IdentifyResult>(faceIdList.Count);
        foreach (var batch in faceIdList.Chunk(10))
        {
            using var request = await CreateRequestAsync(HttpMethod.Post, $"{ApiVersionSegment}/identify", cancellationToken);
            request.Content = JsonContent.Create(new
            {
                dynamicPersonGroupId = _dynamicPersonGroupId,
                faceIds = batch,
                maxNumOfCandidatesReturned = 1,
                confidenceThreshold = 0.6
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "identify faces", cancellationToken);

            var batchResults = await response.Content.ReadFromJsonAsync<List<IdentifyResult>>(cancellationToken: cancellationToken);
            if (batchResults is not null)
            {
                results.AddRange(batchResults);
            }
        }

        return results;
    }

    public async Task EnsureDynamicPersonGroupExistsAsync(CancellationToken cancellationToken)
    {
        using var getRequest = await CreateRequestAsync(
            HttpMethod.Get,
            $"{ApiVersionSegment}/dynamicpersongroups/{_dynamicPersonGroupId}",
            cancellationToken);
        using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken);
        if (getResponse.IsSuccessStatusCode)
        {
            return;
        }
        if (getResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(getResponse, "get dynamic person group", cancellationToken);
        }

        _logger.LogInformation("Dynamic person group {DynamicPersonGroupId} not found, creating it", _dynamicPersonGroupId);
        using var putRequest = await CreateRequestAsync(
            HttpMethod.Put,
            $"{ApiVersionSegment}/dynamicpersongroups/{_dynamicPersonGroupId}",
            cancellationToken);
        putRequest.Content = JsonContent.Create(new
        {
            name = _dynamicPersonGroupId
        });

        using var putResponse = await _httpClient.SendAsync(putRequest, cancellationToken);
        await EnsureSuccessAsync(putResponse, "create dynamic person group", cancellationToken);
    }

    public async Task<string> CreatePersonAsync(string name, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, $"{ApiVersionSegment}/persons", cancellationToken);
        request.Content = JsonContent.Create(new { name });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "create person", cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<CreatePersonResponse>(cancellationToken: cancellationToken);
        var personId = created?.PersonId
            ?? throw new InvalidOperationException("Face API did not return a personId when creating a new person.");
        await WaitForOperationAsync(response, "create person", cancellationToken);
        return personId;
    }

    public async Task AddPersonFaceAsync(
        string personId,
        Stream photo,
        FaceRectangle targetFace,
        CancellationToken cancellationToken)
    {
        var targetFaceValue = $"{targetFace.Left},{targetFace.Top},{targetFace.Width},{targetFace.Height}";
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            $"{ApiVersionSegment}/persons/{personId}/recognitionModels/{RecognitionModel}/persistedfaces" +
            $"?targetFace={targetFaceValue}&detectionModel={DetectionModel}",
            cancellationToken);
        request.Content = new StreamContent(photo) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "add person face", cancellationToken);
        await WaitForOperationAsync(response, "add person face", cancellationToken);
    }

    public async Task AddPersonToDynamicGroupAsync(string personId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Patch,
            $"{ApiVersionSegment}/dynamicpersongroups/{_dynamicPersonGroupId}",
            cancellationToken);
        request.Content = JsonContent.Create(new { addPersonIds = new[] { personId } });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "add person to dynamic person group", cancellationToken);
        await WaitForOperationAsync(response, "add person to dynamic person group", cancellationToken);
    }

    private async Task WaitForOperationAsync(
        HttpResponseMessage initialResponse,
        string operationDescription,
        CancellationToken cancellationToken)
    {
        if (!initialResponse.Headers.TryGetValues("Operation-Location", out var values) ||
            !Uri.TryCreate(values.FirstOrDefault(), UriKind.Absolute, out var operationUri))
        {
            throw new InvalidOperationException(
                $"Face API call to {operationDescription} did not return a valid Operation-Location header.");
        }

        var deadline = DateTimeOffset.UtcNow + _operationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(_operationPollInterval, cancellationToken);

            using var request = await CreateRequestAsync(HttpMethod.Get, operationUri, cancellationToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, $"get {operationDescription} operation status", cancellationToken);

            var status = await response.Content.ReadFromJsonAsync<FaceOperationResult>(cancellationToken: cancellationToken);
            if (string.Equals(status?.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (string.Equals(status?.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Face API {operationDescription} operation failed: {status?.Message ?? "No failure detail was returned."}");
            }
        }

        throw new TimeoutException(
            $"Timed out waiting for Face API {operationDescription} operation after {_operationTimeout}.");
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string relativeUrl, CancellationToken cancellationToken)
    {
        return await CreateRequestAsync(method, new Uri(relativeUrl, UriKind.Relative), cancellationToken);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, Uri requestUri, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, requestUri);
        var token = await _credential.GetTokenAsync(new TokenRequestContext(FaceApiScope), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operationDescription, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Face API call to {operationDescription} failed with status {(int)response.StatusCode} ({response.StatusCode}): {body}");
    }
}
