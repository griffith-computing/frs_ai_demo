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
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;

namespace FaceLab.Core;

public interface IFaceApiClient
{
    FaceLabOptions Options { get; }

    IReadOnlyList<CallTrace> Traces { get; }

    Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(byte[] photo, CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(IEnumerable<string> faceIds, CancellationToken cancellationToken);
    Task EnsureDynamicPersonGroupExistsAsync(CancellationToken cancellationToken);
    Task<string> CreatePersonAsync(string name, CancellationToken cancellationToken);
    Task AddPersonFaceAsync(string personId, byte[] photo, FaceRectangle targetFace, CancellationToken cancellationToken);
    Task AddPersonToDynamicGroupAsync(string personId, CancellationToken cancellationToken);
}

public interface IFaceApiClientFactory
{
    IFaceApiClient Create(FaceLabOptions options);
}

/// <summary>
/// Configuration-driven REST wrapper around Azure AI Face (Detect, Identify and Person
/// Directory). Mirrors the Function App's FaceApiService pipeline, but every parameter comes
/// from <see cref="FaceLabOptions"/> and every HTTP call is captured as a <see cref="CallTrace"/>.
/// One instance corresponds to one run.
/// </summary>
public sealed class FaceApiClient : IFaceApiClient
{
    private static readonly string[] FaceApiScope = { "https://cognitiveservices.azure.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly TokenCredential? _credential;
    private readonly List<CallTrace> _traces = new();

    public FaceApiClient(HttpClient httpClient, FaceLabOptions options, TokenCredential? credential = null)
    {
        _httpClient = httpClient;
        Options = options;
        _credential = credential;

        if (options.AuthMode == FaceAuthMode.DefaultAzureCredential && credential is null)
        {
            throw new ArgumentNullException(nameof(credential), "A token credential is required when the auth mode is DefaultAzureCredential.");
        }

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            _httpClient.BaseAddress = new Uri(options.BaseAddress);
        }
    }

    public FaceLabOptions Options { get; }

    public IReadOnlyList<CallTrace> Traces => _traces;

    public async Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(byte[] photo, CancellationToken cancellationToken)
    {
        var url = $"{Options.ApiVersionSegment}/detect" +
                  $"?returnFaceId=true&recognitionModel={Options.RecognitionModel}&detectionModel={Options.DetectionModel}" +
                  "&returnFaceAttributes=headPose,mask,qualityForRecognition&returnFaceLandmarks=true&returnRecognitionModel=true";

        var body = await SendAsync(
            HttpMethod.Post,
            url,
            "detect faces",
            () => CreateImageContent(photo),
            $"{photo.Length} byte image",
            cancellationToken);

        return Deserialize<List<DetectedFace>>(body) ?? new List<DetectedFace>();
    }

    public async Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(IEnumerable<string> faceIds, CancellationToken cancellationToken)
    {
        var faceIdList = faceIds.ToList();
        if (faceIdList.Count == 0)
        {
            return Array.Empty<IdentifyResult>();
        }

        var results = new List<IdentifyResult>(faceIdList.Count);
        foreach (var batch in faceIdList.Chunk(Options.IdentifyBatchSize))
        {
            var payload = new
            {
                dynamicPersonGroupId = Options.DynamicPersonGroupId,
                faceIds = batch,
                maxNumOfCandidatesReturned = Options.MaxCandidatesReturned,
                confidenceThreshold = Options.ConfidenceThreshold
            };

            var body = await SendJsonAsync(
                HttpMethod.Post,
                $"{Options.ApiVersionSegment}/identify",
                "identify faces",
                payload,
                cancellationToken);

            var batchResults = Deserialize<List<IdentifyResult>>(body);
            if (batchResults is not null)
            {
                results.AddRange(batchResults);
            }
        }

        return results;
    }

    public async Task EnsureDynamicPersonGroupExistsAsync(CancellationToken cancellationToken)
    {
        var url = $"{Options.ApiVersionSegment}/dynamicpersongroups/{Options.DynamicPersonGroupId}";

        var (response, _, _) = await SendRawAsync(HttpMethod.Get, url, "get dynamic person group", null, null, cancellationToken);
        if (response.IsSuccess)
        {
            return;
        }

        if (response.StatusCode != (int)HttpStatusCode.NotFound)
        {
            throw FailureException("get dynamic person group", response);
        }

        await SendJsonAsync(
            HttpMethod.Put,
            url,
            "create dynamic person group",
            new { name = Options.DynamicPersonGroupId },
            cancellationToken);
    }

    public async Task<string> CreatePersonAsync(string name, CancellationToken cancellationToken)
    {
        var (trace, operationLocation, responseBody) = await SendTracedJsonAsync(
            HttpMethod.Post,
            $"{Options.ApiVersionSegment}/persons",
            "create person",
            new { name },
            cancellationToken);

        var personId = Deserialize<CreatePersonResponse>(responseBody)?.PersonId
            ?? throw new FaceApiException("Face API did not return a personId when creating a new person.");

        await WaitForOperationAsync(operationLocation, "create person", cancellationToken);
        return personId;
    }

    public async Task AddPersonFaceAsync(string personId, byte[] photo, FaceRectangle targetFace, CancellationToken cancellationToken)
    {
        var targetFaceValue = $"{targetFace.Left},{targetFace.Top},{targetFace.Width},{targetFace.Height}";
        var url = $"{Options.ApiVersionSegment}/persons/{personId}/recognitionModels/{Options.RecognitionModel}/persistedfaces" +
                  $"?targetFace={targetFaceValue}&detectionModel={Options.DetectionModel}";

        var (_, operationLocation, _) = await SendRawAsync(
            HttpMethod.Post,
            url,
            "add person face",
            () => CreateImageContent(photo),
            $"{photo.Length} byte image, targetFace={targetFaceValue}",
            cancellationToken,
            throwOnFailure: true);

        await WaitForOperationAsync(operationLocation, "add person face", cancellationToken);
    }

    public async Task AddPersonToDynamicGroupAsync(string personId, CancellationToken cancellationToken)
    {
        var (_, operationLocation, _) = await SendTracedJsonAsync(
            HttpMethod.Patch,
            $"{Options.ApiVersionSegment}/dynamicpersongroups/{Options.DynamicPersonGroupId}",
            "add person to dynamic person group",
            new { addPersonIds = new[] { personId } },
            cancellationToken);

        await WaitForOperationAsync(operationLocation, "add person to dynamic person group", cancellationToken);
    }

    private async Task WaitForOperationAsync(string? operationLocation, string operationDescription, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(operationLocation, UriKind.Absolute, out var operationUri))
        {
            throw new FaceApiException(
                $"Face API call to {operationDescription} did not return a valid Operation-Location header.");
        }

        var deadline = DateTimeOffset.UtcNow + Options.OperationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(Options.OperationPollInterval, cancellationToken);

            var body = await SendAsync(
                HttpMethod.Get,
                operationUri.ToString(),
                $"get {operationDescription} operation status",
                null,
                null,
                cancellationToken);

            var status = Deserialize<FaceOperationResult>(body);
            if (string.Equals(status?.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(status?.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new FaceApiException(
                    $"Face API {operationDescription} operation failed: {status?.Message ?? "No failure detail was returned."}");
            }
        }

        throw new FaceApiException(
            $"Timed out waiting for Face API {operationDescription} operation after {Options.OperationTimeout}.");
    }

    private Task<string?> SendJsonAsync(
        HttpMethod method,
        string url,
        string operationDescription,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        return SendAsync(
            method,
            url,
            operationDescription,
            () => new StringContent(json, Encoding.UTF8, "application/json"),
            json,
            cancellationToken);
    }

    private async Task<(CallTrace Trace, string? OperationLocation, string ResponseBody)> SendTracedJsonAsync(
        HttpMethod method,
        string url,
        string operationDescription,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        return await SendRawAsync(
            method,
            url,
            operationDescription,
            () => new StringContent(json, Encoding.UTF8, "application/json"),
            json,
            cancellationToken,
            throwOnFailure: true);
    }

    private async Task<string?> SendAsync(
        HttpMethod method,
        string url,
        string operationDescription,
        Func<HttpContent>? contentFactory,
        string? requestSummary,
        CancellationToken cancellationToken)
    {
        var (_, _, responseBody) = await SendRawAsync(
            method,
            url,
            operationDescription,
            contentFactory,
            requestSummary,
            cancellationToken,
            throwOnFailure: true);
        return responseBody;
    }

    private async Task<(CallTrace Trace, string? OperationLocation, string ResponseBody)> SendRawAsync(
        HttpMethod method,
        string url,
        string operationDescription,
        Func<HttpContent>? contentFactory,
        string? requestSummary,
        CancellationToken cancellationToken,
        bool throwOnFailure = false)
    {
        var requestUri = Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(url, UriKind.Relative);

        var trace = new CallTrace
        {
            Operation = operationDescription,
            Method = method.Method,
            Url = requestUri.IsAbsoluteUri ? requestUri.ToString() : Options.BaseAddress + url,
            RequestSummary = CallTrace.Truncate(requestSummary)
        };
        _traces.Add(trace);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(method, requestUri);
            if (contentFactory is not null)
            {
                request.Content = contentFactory();
            }

            await ApplyAuthenticationAsync(request, cancellationToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            trace.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            trace.StatusCode = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            trace.ResponseBody = CallTrace.Truncate(responseBody);

            var operationLocation = response.Headers.TryGetValues("Operation-Location", out var values)
                ? values.FirstOrDefault()
                : null;

            if (throwOnFailure && !response.IsSuccessStatusCode)
            {
                throw FailureException(operationDescription, trace);
            }

            return (trace, operationLocation, responseBody);
        }
        catch (Exception ex) when (ex is not FaceApiException)
        {
            stopwatch.Stop();
            trace.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            trace.Error = ex.Message;
            throw new FaceApiException($"Face API call to {operationDescription} failed: {ex.Message}", ex);
        }
    }

    private async Task ApplyAuthenticationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Options.AuthMode == FaceAuthMode.SubscriptionKey)
        {
            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", Options.SubscriptionKey);
            return;
        }

        var token = await _credential!.GetTokenAsync(new TokenRequestContext(FaceApiScope), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    private static HttpContent CreateImageContent(byte[] photo) =>
        new ByteArrayContent(photo)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
        };

    private static T? Deserialize<T>(string? body) =>
        string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body);

    private static FaceApiException FailureException(string operationDescription, CallTrace trace)
    {
        trace.Error = $"HTTP {trace.StatusCode}";
        return new FaceApiException(
            $"Face API call to {operationDescription} failed with status {trace.StatusCode}: {trace.ResponseBody}");
    }
}

public sealed class FaceApiException : Exception
{
    public FaceApiException(string message)
        : base(message)
    {
    }

    public FaceApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
