namespace FrsAiDemo.FunctionApp.Models;

/// <summary>
/// Lightweight event published to Event Hub after a photo is uploaded to Blob Storage.
/// Kept small deliberately since Event Hub is not suited to carrying binary payloads.
/// </summary>
public sealed class PhotoUploadedEvent
{
    public required string UploadId { get; init; }
    public required string BlobUrl { get; init; }
    public required string ContainerName { get; init; }
    public required string BlobName { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
