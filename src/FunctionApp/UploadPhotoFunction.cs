using System.Net;
using System.Text.Json;
using FrsAiDemo.FunctionApp.Models;
using FrsAiDemo.FunctionApp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrsAiDemo.FunctionApp;

/// <summary>
/// HTTP-triggered entry point for photo uploads. Stores the photo in Blob Storage and
/// publishes a lightweight event (blob URL + metadata) to Event Hub for asynchronous
/// processing by <see cref="ProcessPhotoFunction"/>. Event Hub is not used to carry the
/// photo bytes directly because of Event Hub message size limits (1-20MB depending on tier).
/// </summary>
public sealed class UploadPhotoFunction
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly string _containerName;
    private readonly ILogger<UploadPhotoFunction> _logger;

    public UploadPhotoFunction(IBlobStorageService blobStorageService, IConfiguration configuration, ILogger<UploadPhotoFunction> logger)
    {
        _blobStorageService = blobStorageService;
        _containerName = configuration["PhotosContainerName"] ?? "photos";
        _logger = logger;
    }

    [Function("UploadPhotoFunction")]
    public async Task<UploadPhotoOutput> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "photos")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger<UploadPhotoFunction>();

        if (req.Body is null || req.Body.Length == 0)
        {
            logger.LogWarning("Upload request received with an empty body.");
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Request body must contain photo bytes.");
            return new UploadPhotoOutput { HttpResponse = badRequest, EventHubMessage = string.Empty };
        }

        var uploadId = Guid.NewGuid().ToString("N");
        var contentType = req.Headers.TryGetValues("Content-Type", out var values)
            ? values.FirstOrDefault() ?? "application/octet-stream"
            : "application/octet-stream";
        var extension = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var blobName = $"{uploadId}{extension}";

        var blobUrl = await _blobStorageService.UploadPhotoAsync(blobName, req.Body, contentType, executionContext.CancellationToken);
        logger.LogInformation("Uploaded photo {BlobName} to {BlobUrl}", blobName, blobUrl);

        var uploadEvent = new PhotoUploadedEvent
        {
            UploadId = uploadId,
            BlobUrl = blobUrl,
            ContainerName = _containerName,
            BlobName = blobName
        };

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(uploadEvent);

        return new UploadPhotoOutput
        {
            HttpResponse = response,
            EventHubMessage = JsonSerializer.Serialize(uploadEvent)
        };
    }
}

/// <summary>Multi-output binding wrapper: HTTP response back to the caller plus the Event Hub message.</summary>
public sealed class UploadPhotoOutput
{
    [HttpResult]
    public required HttpResponseData HttpResponse { get; init; }

    [EventHubOutput("photo-events", Connection = "EventHub")]
    public required string EventHubMessage { get; init; }
}
