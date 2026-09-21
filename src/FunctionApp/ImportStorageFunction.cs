//----------------------------------------------------------------------------------
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//----------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FrsAiDemo.FunctionApp.Models;
using FrsAiDemo.FunctionApp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrsAiDemo.FunctionApp;

public sealed class ImportStorageFunction(
    IUploadRepository repository,
    BlobServiceClient destinationStorage,
    EventHubProducerClient eventHub,
    TokenCredential credential,
    IConfiguration configuration,
    ILogger<ImportStorageFunction> logger)
{
    private static readonly byte[] JpegSignature = [0xff, 0xd8, 0xff];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    [Function("ImportStorageFunction")]
    public async Task RunAsync(
        [TimerTrigger("%BulkUploadsImportSchedule%")] TimerInfo timer,
        FunctionContext context)
    {
        if (!configuration.GetValue<bool>("BulkUploads:Enabled"))
        {
            return;
        }

        var import = await repository.GetNextImportAsync(context.CancellationToken);
        if (import is null)
        {
            return;
        }

        try
        {
            ValidateAllowlist(import);
            import.Status = "Running";
            await repository.UpdateImportAsync(import, context.CancellationToken);
            await ProcessPageAsync(import, context.CancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Storage import {ImportId} failed", import.Id);
            import.ConsecutiveFailureCount++;
            import.Status = import.ConsecutiveFailureCount >= 3 ? "Failed" : "Running";
            import.FailureSummary = import.Status == "Failed"
                ? "The source container could not be imported after three attempts."
                : "The current import page will be retried.";
            await repository.UpdateImportAsync(import, CancellationToken.None);
        }
    }

    internal async Task ProcessPageAsync(StorageImportRecord import, CancellationToken cancellationToken)
    {
        var sourceService = new BlobServiceClient(
            new Uri($"https://{import.SourceAccountName}.blob.core.windows.net"),
            credential);
        var sourceContainer = sourceService.GetBlobContainerClient(import.SourceContainerName);
        var batchSize = Math.Clamp(configuration.GetValue("BulkUploads:MaxFiles", 100), 1, 100);
        var pages = sourceContainer
            .GetBlobsAsync(BlobTraits.None, BlobStates.None, cancellationToken: cancellationToken)
            .AsPages(import.ContinuationToken, batchSize);

        await foreach (var page in pages.WithCancellation(cancellationToken))
        {
            var eligible = page.Values
                .Where(x => IsSupportedExtension(x.Name))
                .Take(batchSize)
                .ToList();

            if (eligible.Count > 0)
            {
                var batchId = $"{import.Id}-batch-{import.NextBatchNumber:D6}";
                var now = DateTimeOffset.UtcNow;
                await repository.CreateBatchAsync(new UploadBatchRecord
                {
                    Id = batchId,
                    ImportId = import.Id,
                    OwnerObjectId = import.OwnerObjectId,
                    ExpectedFileCount = eligible.Count,
                    SubmittedFileCount = 0,
                    SubmissionCompleted = false,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }, cancellationToken);

                foreach (var blob in eligible)
                {
                    try
                    {
                        await QueueBlobAsync(import, batchId, sourceContainer, blob, cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        if (IsTransient(exception))
                        {
                            throw;
                        }
                        logger.LogError(
                            exception,
                            "Failed to queue blob {BlobName} for import {ImportId}",
                            blob.Name,
                            import.Id);
                        var uploadId = CreateDeterministicId(import.Id, blob.Name);
                        var extension = Path.GetExtension(blob.Name).ToLowerInvariant();
                        var contentType = extension == ".png" ? "image/png" : "image/jpeg";
                        var created = await repository.TryCreateAsync(
                            CreateFailed(
                                uploadId,
                                import,
                                batchId,
                                blob.Name,
                                contentType,
                                DateTimeOffset.UtcNow,
                                "The source photo could not be queued for analysis."),
                            CancellationToken.None);
                        if (!created)
                        {
                            var existing = await repository.GetAsync(uploadId, CancellationToken.None);
                            if (existing?.Status == UploadStatuses.Queued)
                            {
                                await repository.SetStatusAsync(
                                    uploadId,
                                    UploadStatuses.Failed,
                                    detectedFaceCount: null,
                                    failureSummary: "The source photo could not be queued for analysis.",
                                    CancellationToken.None);
                            }
                        }
                    }
                }

                await repository.CreateBatchAsync(new UploadBatchRecord
                {
                    Id = batchId,
                    ImportId = import.Id,
                    OwnerObjectId = import.OwnerObjectId,
                    ExpectedFileCount = eligible.Count,
                    SubmittedFileCount = eligible.Count,
                    SubmissionCompleted = true,
                    CreatedUtc = now,
                    UpdatedUtc = DateTimeOffset.UtcNow
                }, cancellationToken);
                import.NextBatchNumber++;
                import.DiscoveredFileCount += eligible.Count;
            }

            import.ContinuationToken = page.ContinuationToken;
            import.Status = page.ContinuationToken is null ? "Completed" : "Running";
            import.ConsecutiveFailureCount = 0;
            import.FailureSummary = null;
            await repository.UpdateImportAsync(import, cancellationToken);
            break;
        }
    }

    private async Task QueueBlobAsync(
        StorageImportRecord import,
        string batchId,
        BlobContainerClient sourceContainer,
        BlobItem blob,
        CancellationToken cancellationToken)
    {
        var uploadId = CreateDeterministicId(import.Id, blob.Name);
        var prior = await repository.GetAsync(uploadId, cancellationToken);
        if (prior is not null)
        {
            if (prior.Status == UploadStatuses.Queued)
            {
                await PublishAsync(new PhotoUploadedEvent
                {
                    UploadId = prior.Id,
                    BlobUrl = prior.BlobUrl,
                    ContainerName = prior.ContainerName,
                    BlobName = prior.BlobName,
                    StorageAccountName = prior.SourceAccountName,
                    TimestampUtc = prior.CreatedUtc
                }, cancellationToken);
            }
            return;
        }

        var extension = Path.GetExtension(blob.Name).ToLowerInvariant() == ".jpeg" ? ".jpg" : Path.GetExtension(blob.Name).ToLowerInvariant();
        var contentType = extension == ".png" ? "image/png" : "image/jpeg";
        var now = DateTimeOffset.UtcNow;

        if (blob.Properties.ContentLength is <= 0 or > 6 * 1024 * 1024)
        {
            await repository.TryCreateAsync(CreateFailed(
                uploadId, import, batchId, blob.Name, contentType, now, "Photo size must be between 1 byte and 6 MB."), cancellationToken);
            return;
        }

        var sourceBlob = sourceContainer.GetBlobClient(blob.Name);
        await using var source = await sourceBlob.OpenReadAsync(cancellationToken: cancellationToken);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        if (!HasExpectedSignature(buffer, extension))
        {
            await repository.TryCreateAsync(CreateFailed(
                uploadId, import, batchId, blob.Name, contentType, now, "The file content does not match its JPEG or PNG type."), cancellationToken);
            return;
        }

        string containerName;
        string blobName;
        string blobUrl;
        string? sourceAccountName;
        if (import.Mode == "copy")
        {
            containerName = configuration["PhotosContainerName"] ?? "photos";
            blobName = $"{uploadId}{extension}";
            var destination = destinationStorage.GetBlobContainerClient(containerName).GetBlobClient(blobName);
            buffer.Position = 0;
            await destination.UploadAsync(buffer, overwrite: true, cancellationToken);
            await destination.SetHttpHeadersAsync(
                new BlobHttpHeaders { ContentType = contentType },
                cancellationToken: cancellationToken);
            blobUrl = destination.Uri.ToString();
            sourceAccountName = null;
        }
        else
        {
            containerName = import.SourceContainerName;
            blobName = blob.Name;
            blobUrl = sourceBlob.Uri.ToString();
            sourceAccountName = import.SourceAccountName;
        }

        var upload = new UploadRecord
        {
            Id = uploadId,
            Status = UploadStatuses.Queued,
            ContainerName = containerName,
            BlobName = blobName,
            BlobUrl = blobUrl,
            ContentType = contentType,
            BatchId = batchId,
            ImportId = import.Id,
            OriginalFileName = blob.Name,
            SourceAccountName = sourceAccountName,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var created = await repository.TryCreateAsync(upload, cancellationToken);
        var existing = created ? upload : await repository.GetAsync(uploadId, cancellationToken);
        if (existing?.Status != UploadStatuses.Queued)
        {
            return;
        }

        var uploadEvent = new PhotoUploadedEvent
        {
            UploadId = uploadId,
            BlobUrl = blobUrl,
            ContainerName = containerName,
            BlobName = blobName,
            StorageAccountName = sourceAccountName,
            TimestampUtc = now
        };
        await PublishAsync(uploadEvent, cancellationToken);
    }

    private async Task PublishAsync(PhotoUploadedEvent uploadEvent, CancellationToken cancellationToken)
    {
        using var eventBatch = await eventHub.CreateBatchAsync(cancellationToken);
        if (!eventBatch.TryAdd(new EventData(BinaryData.FromString(JsonSerializer.Serialize(uploadEvent)))))
        {
            throw new InvalidOperationException("The import event exceeded the Event Hub batch limit.");
        }
        await eventHub.SendAsync(eventBatch, cancellationToken);
    }

    private void ValidateAllowlist(StorageImportRecord import)
    {
        var json = configuration["BulkUploads:SourcesJson"] ?? "[]";
        var sources = JsonSerializer.Deserialize<List<ImportSource>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        var source = sources.SingleOrDefault(x =>
            x.Key == import.SourceKey
            && x.AccountName == import.SourceAccountName
            && x.ContainerName == import.SourceContainerName);
        var allowed = import.Mode == "copy" ? source?.AllowCopy == true : import.Mode == "inplace" && source?.AllowInPlace == true;
        if (!allowed)
        {
            throw new InvalidOperationException("The storage source or import mode is not allowlisted.");
        }
    }

    private static UploadRecord CreateFailed(
        string uploadId,
        StorageImportRecord import,
        string batchId,
        string fileName,
        string contentType,
        DateTimeOffset now,
        string failure) => new()
        {
            Id = uploadId,
            Status = UploadStatuses.Failed,
            ContainerName = import.SourceContainerName,
            BlobName = fileName,
            BlobUrl = string.Empty,
            ContentType = contentType,
            BatchId = batchId,
            ImportId = import.Id,
            OriginalFileName = fileName,
            SourceAccountName = import.SourceAccountName,
            FailureSummary = failure,
            CreatedUtc = now,
            UpdatedUtc = now
        };

    private static bool HasExpectedSignature(MemoryStream stream, string extension)
    {
        var expected = extension == ".png" ? PngSignature : JpegSignature;
        var bytes = stream.GetBuffer().AsSpan(0, (int)stream.Length);
        return bytes.Length >= expected.Length && bytes[..expected.Length].SequenceEqual(expected);
    }

    private static bool IsSupportedExtension(string name) =>
        Path.GetExtension(name).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(name).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(name).Equals(".png", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransient(Exception exception) =>
        exception is EventHubsException
        || exception is Azure.RequestFailedException { Status: 408 or 429 or >= 500 };

    private static string CreateDeterministicId(string importId, string blobName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{importId}|{blobName}"));
        return $"upload-{Convert.ToHexString(hash).ToLowerInvariant()[..32]}";
    }

    private sealed class ImportSource
    {
        public required string Key { get; init; }
        public required string AccountName { get; init; }
        public required string ContainerName { get; init; }
        public bool AllowCopy { get; init; }
        public bool AllowInPlace { get; init; }
    }
}
