using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FrsAiDemo.WebApp.Models;

namespace FrsAiDemo.WebApp.Services;

public interface IUploadService
{
    Task<UploadAccepted> UploadAsync(IFormFile photo, CancellationToken cancellationToken);
}

public sealed record UploadAccepted(string UploadId);

public sealed class UploadService : IUploadService
{
    private static readonly byte[] JpegSignature = [0xff, 0xd8, 0xff];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private readonly BlobServiceClient _blobServiceClient;
    private readonly EventHubProducerClient _eventHubProducerClient;
    private readonly IFaceReviewRepository _repository;
    private readonly string _containerName;
    private readonly long _maxBytes;

    public UploadService(
        BlobServiceClient blobServiceClient,
        EventHubProducerClient eventHubProducerClient,
        IFaceReviewRepository repository,
        IConfiguration configuration)
    {
        _blobServiceClient = blobServiceClient;
        _eventHubProducerClient = eventHubProducerClient;
        _repository = repository;
        _containerName = configuration["PhotosContainerName"] ?? "photos";
        _maxBytes = configuration.GetValue<long?>("Uploads:MaxBytes") ?? 10 * 1024 * 1024;
    }

    public async Task<UploadAccepted> UploadAsync(IFormFile photo, CancellationToken cancellationToken)
    {
        if (photo.Length == 0 || photo.Length > _maxBytes)
        {
            throw new ValidationException($"Photo size must be between 1 byte and {_maxBytes / 1024 / 1024} MB.");
        }

        var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();
        var contentType = photo.ContentType.ToLowerInvariant();
        var expectedSignature = (extension, contentType) switch
        {
            (".jpg" or ".jpeg", "image/jpeg") => JpegSignature,
            (".png", "image/png") => PngSignature,
            _ => throw new ValidationException("Only JPEG and PNG photos are supported.")
        };

        await using var source = photo.OpenReadStream();
        var signature = new byte[expectedSignature.Length];
        var bytesRead = await source.ReadAsync(signature, cancellationToken);
        if (bytesRead != expectedSignature.Length || !signature.SequenceEqual(expectedSignature))
        {
            throw new ValidationException("The file content does not match its JPEG or PNG type.");
        }
        source.Position = 0;

        var uploadId = Guid.NewGuid().ToString("N");
        var blobName = $"{uploadId}{(extension == ".jpeg" ? ".jpg" : extension)}";
        var blobClient = _blobServiceClient.GetBlobContainerClient(_containerName).GetBlobClient(blobName);
        var now = DateTimeOffset.UtcNow;

        await blobClient.UploadAsync(source, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, cancellationToken);

        await _repository.CreateUploadAsync(new UploadRecord
        {
            Id = uploadId,
            Status = "Queued",
            ContainerName = _containerName,
            BlobName = blobName,
            BlobUrl = blobClient.Uri.ToString(),
            ContentType = contentType,
            CreatedUtc = now,
            UpdatedUtc = now
        }, cancellationToken);

        var uploadEvent = new
        {
            UploadId = uploadId,
            BlobUrl = blobClient.Uri.ToString(),
            ContainerName = _containerName,
            BlobName = blobName,
            TimestampUtc = now
        };

        try
        {
            using var batch = await _eventHubProducerClient.CreateBatchAsync(cancellationToken);
            if (!batch.TryAdd(new EventData(BinaryData.FromString(JsonSerializer.Serialize(uploadEvent)))))
            {
                throw new InvalidOperationException("The upload event exceeded the Event Hub batch limit.");
            }
            await _eventHubProducerClient.SendAsync(batch, cancellationToken);
        }
        catch
        {
            await _repository.SetUploadStatusAsync(uploadId, "Failed", "Photo could not be queued for analysis.", CancellationToken.None);
            throw;
        }

        return new UploadAccepted(uploadId);
    }
}