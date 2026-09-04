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

using System.Text.Json;
using FrsAiDemo.FunctionApp.Models;
using FrsAiDemo.FunctionApp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FrsAiDemo.FunctionApp;

/// <summary>
/// Event Hub-triggered function: reads a photo-uploaded event, fetches the blob, detects faces
/// via Azure AI Face API, and either records a repeat sighting or registers a brand-new person.
/// </summary>
public sealed class ProcessPhotoFunction
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly IFaceApiService _faceApiService;
    private readonly ICosmosFaceRepository _faceRepository;
    private readonly IUploadRepository _uploadRepository;
    private readonly ILogger<ProcessPhotoFunction> _logger;

    public ProcessPhotoFunction(
        IBlobStorageService blobStorageService,
        IFaceApiService faceApiService,
        ICosmosFaceRepository faceRepository,
        IUploadRepository uploadRepository,
        ILogger<ProcessPhotoFunction> logger)
    {
        _blobStorageService = blobStorageService;
        _faceApiService = faceApiService;
        _faceRepository = faceRepository;
        _uploadRepository = uploadRepository;
        _logger = logger;
    }

    [Function("ProcessPhotoFunction")]
    public async Task RunAsync(
        [EventHubTrigger("photo-events", Connection = "EventHub", ConsumerGroup = "%EventHubConsumerGroup%", IsBatched = false)] string message,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger<ProcessPhotoFunction>();
        var cancellationToken = executionContext.CancellationToken;

        PhotoUploadedEvent? uploadEvent;
        try
        {
            uploadEvent = JsonSerializer.Deserialize<PhotoUploadedEvent>(message);
        }
        catch (JsonException ex)
        {
            // Poison message: log and drop rather than retrying forever.
            logger.LogError(ex, "Failed to deserialize photo-uploaded event, dropping message: {Message}", message);
            return;
        }

        if (uploadEvent is null)
        {
            logger.LogError("Deserialized photo-uploaded event was null, dropping message: {Message}", message);
            return;
        }

        logger.LogInformation("Processing photo {BlobName} from upload {UploadId}", uploadEvent.BlobName, uploadEvent.UploadId);

        try
        {
            await _uploadRepository.SetStatusAsync(
                uploadEvent.UploadId,
                UploadStatuses.Processing,
                detectedFaceCount: null,
                failureSummary: null,
                cancellationToken);
            await ProcessUploadEventAsync(uploadEvent, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unexpected failures (Face API/Cosmos/Storage errors) are logged and the raw event
            // is dead-lettered to a "poison-messages" blob container for manual investigation,
            // rather than retried indefinitely (Event Hub triggers have no built-in poison queue).
            logger.LogError(ex, "Failed to process photo-uploaded event for upload {UploadId}, dead-lettering message", uploadEvent.UploadId);
            try
            {
                await _uploadRepository.SetStatusAsync(
                    uploadEvent.UploadId,
                    UploadStatuses.Failed,
                    detectedFaceCount: null,
                    failureSummary: "Photo analysis failed.",
                    CancellationToken.None);
            }
            catch (Exception statusException)
            {
                logger.LogError(statusException, "Failed to persist failure status for upload {UploadId}", uploadEvent.UploadId);
            }

            await _blobStorageService.UploadPoisonMessageAsync(
                $"{uploadEvent.UploadId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json",
                JsonSerializer.Serialize(new { uploadEvent, error = ex.ToString() }),
                CancellationToken.None);
        }
    }

    internal async Task ProcessUploadEventAsync(PhotoUploadedEvent uploadEvent, CancellationToken cancellationToken)
    {
        await _faceApiService.EnsureDynamicPersonGroupExistsAsync(cancellationToken);

        // StreamContent owns and disposes its input, so each Face API request needs a fresh stream.
        await using var photoStream = await _blobStorageService.DownloadPhotoAsync(uploadEvent.ContainerName, uploadEvent.BlobName, cancellationToken);
        using var buffered = new MemoryStream();
        await photoStream.CopyToAsync(buffered, cancellationToken);
        var photoBytes = buffered.ToArray();

        using var detectionStream = new MemoryStream(photoBytes, writable: false);
        var detectedFaces = await _faceApiService.DetectFacesAsync(detectionStream, cancellationToken);

        if (detectedFaces.Count == 0)
        {
            _logger.LogInformation("No faces detected in photo {BlobName}", uploadEvent.BlobName);
            await _uploadRepository.SetStatusAsync(
                uploadEvent.UploadId,
                UploadStatuses.NoFaces,
                detectedFaces.Count,
                failureSummary: null,
                cancellationToken);
            return;
        }

        var faceIds = detectedFaces.Where(f => f.FaceId is not null).Select(f => f.FaceId!).ToList();
        var identifyResults = await _faceApiService.IdentifyAsync(faceIds, cancellationToken);
        var identifiedByFaceId = identifyResults.ToDictionary(r => r.FaceId ?? string.Empty, r => r);

        foreach (var face in detectedFaces)
        {
            if (face.FaceId is null)
            {
                continue;
            }

            var matchedPersonId = identifiedByFaceId.TryGetValue(face.FaceId, out var identifyResult)
                ? identifyResult.Candidates.OrderByDescending(c => c.Confidence).FirstOrDefault()
                : null;

            var sighting = new RecognitionEvent
            {
                BlobUrl = uploadEvent.BlobUrl,
                Confidence = matchedPersonId?.Confidence ?? 0,
                FaceId = face.FaceId,
                TimestampUtc = uploadEvent.TimestampUtc
            };

            if (matchedPersonId?.PersonId is not null)
            {
                _logger.LogInformation("Recognized existing person {PersonId} in photo {BlobName}", matchedPersonId.PersonId, uploadEvent.BlobName);
                await _faceRepository.RecordRecognitionAsync(matchedPersonId.PersonId, sighting, cancellationToken);
            }
            else
            {
                await RegisterNewPersonAsync(uploadEvent, sighting, face.FaceRectangle, photoBytes, cancellationToken);
            }
        }

        await _uploadRepository.SetStatusAsync(
            uploadEvent.UploadId,
            UploadStatuses.Completed,
            detectedFaces.Count,
            failureSummary: null,
            cancellationToken);
    }

    private async Task RegisterNewPersonAsync(
        PhotoUploadedEvent uploadEvent,
        RecognitionEvent sighting,
        FaceRectangle? faceRectangle,
        byte[] photoBytes,
        CancellationToken cancellationToken)
    {
        if (faceRectangle is null)
        {
            throw new InvalidOperationException($"Face API did not return a rectangle for face {sighting.FaceId}.");
        }

        var personId = await _faceApiService.CreatePersonAsync($"person-{Guid.NewGuid():N}", cancellationToken);

        using var enrollmentStream = new MemoryStream(photoBytes, writable: false);
        await _faceApiService.AddPersonFaceAsync(personId, enrollmentStream, faceRectangle, cancellationToken);
        await _faceApiService.AddPersonToDynamicGroupAsync(personId, cancellationToken);

        await _faceRepository.CreateFaceRecordAsync(
            personId,
            _faceApiService.DynamicPersonGroupId,
            sighting,
            cancellationToken);
        _logger.LogInformation("Registered new person {PersonId} from photo {BlobName}", personId, uploadEvent.BlobName);
    }
}
