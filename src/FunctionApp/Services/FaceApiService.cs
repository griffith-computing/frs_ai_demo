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
    string PersonGroupId { get; }

    Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(Stream photo, CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(IEnumerable<string> faceIds, CancellationToken cancellationToken);
    Task EnsurePersonGroupExistsAsync(CancellationToken cancellationToken);
    Task<string> CreatePersonAsync(string name, CancellationToken cancellationToken);
    Task AddPersonFaceAsync(string personId, Stream photo, CancellationToken cancellationToken);
    Task TrainPersonGroupAsync(CancellationToken cancellationToken);
    Task<bool> WaitForTrainingCompletedAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Thin REST wrapper around the Azure AI Face API (Detect / PersonGroup / Person / Identify /
/// Train). A hand-rolled HttpClient wrapper is used instead of the old (deprecated)
/// Cognitive Services Face SDK. Auth uses Azure AD bearer tokens obtained via the
/// TokenCredential registered in DI (DefaultAzureCredential / managed identity in Azure).
/// </summary>
public sealed class FaceApiService : IFaceApiService
{
    private static readonly string[] FaceApiScope = { "https://cognitiveservices.azure.com/.default" };
    private const string ApiVersionSegment = "face/v1.0";

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _personGroupId;
    private readonly ILogger<FaceApiService> _logger;

    public string PersonGroupId => _personGroupId;

    public FaceApiService(HttpClient httpClient, TokenCredential credential, IConfiguration configuration, ILogger<FaceApiService> logger)
    {
        _httpClient = httpClient;
        _credential = credential;
        _logger = logger;

        var endpoint = configuration["FaceApi:Endpoint"]
            ?? throw new InvalidOperationException("FaceApi:Endpoint configuration value is required.");
        _httpClient.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        _personGroupId = configuration["FaceApi:PersonGroupId"] ?? "frs-ai-demo-group";
    }

    public async Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(Stream photo, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            $"{ApiVersionSegment}/detect?returnFaceId=true&recognitionModel=recognition_04&detectionModel=detection_03",
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

        using var request = await CreateRequestAsync(HttpMethod.Post, $"{ApiVersionSegment}/identify", cancellationToken);
        request.Content = JsonContent.Create(new
        {
            personGroupId = _personGroupId,
            faceIds = faceIdList,
            maxNumOfCandidatesReturned = 1,
            confidenceThreshold = 0.6
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "identify faces", cancellationToken);

        var results = await response.Content.ReadFromJsonAsync<List<IdentifyResult>>(cancellationToken: cancellationToken);
        return results ?? new List<IdentifyResult>();
    }

    public async Task EnsurePersonGroupExistsAsync(CancellationToken cancellationToken)
    {
        using var getRequest = await CreateRequestAsync(HttpMethod.Get, $"{ApiVersionSegment}/persongroups/{_personGroupId}", cancellationToken);
        using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken);
        if (getResponse.IsSuccessStatusCode)
        {
            return;
        }

        _logger.LogInformation("PersonGroup {PersonGroupId} not found, creating it", _personGroupId);
        using var putRequest = await CreateRequestAsync(HttpMethod.Put, $"{ApiVersionSegment}/persongroups/{_personGroupId}", cancellationToken);
        putRequest.Content = JsonContent.Create(new
        {
            name = _personGroupId,
            recognitionModel = "recognition_04"
        });

        using var putResponse = await _httpClient.SendAsync(putRequest, cancellationToken);
        await EnsureSuccessAsync(putResponse, "create person group", cancellationToken);
    }

    public async Task<string> CreatePersonAsync(string name, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, $"{ApiVersionSegment}/persongroups/{_personGroupId}/persons", cancellationToken);
        request.Content = JsonContent.Create(new { name });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "create person", cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<CreatePersonResponse>(cancellationToken: cancellationToken);
        return created?.PersonId ?? throw new InvalidOperationException("Face API did not return a personId when creating a new person.");
    }

    public async Task AddPersonFaceAsync(string personId, Stream photo, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            $"{ApiVersionSegment}/persongroups/{_personGroupId}/persons/{personId}/persistedfaces",
            cancellationToken);
        request.Content = new StreamContent(photo) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "add person face", cancellationToken);
    }

    public async Task TrainPersonGroupAsync(CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, $"{ApiVersionSegment}/persongroups/{_personGroupId}/train", cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "train person group", cancellationToken);
    }

    public async Task<bool> WaitForTrainingCompletedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, $"{ApiVersionSegment}/persongroups/{_personGroupId}/training", cancellationToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "get training status", cancellationToken);

            var status = await response.Content.ReadFromJsonAsync<PersonGroupTrainingStatus>(cancellationToken: cancellationToken);
            if (string.Equals(status?.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(status?.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("PersonGroup training failed: {Message}", status?.Message);
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        _logger.LogWarning("Timed out waiting for PersonGroup training to complete after {Timeout}", timeout);
        return false;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string relativeUrl, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
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
