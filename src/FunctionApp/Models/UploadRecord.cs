using System.Text.Json.Serialization;

namespace FrsAiDemo.FunctionApp.Models;

public static class UploadStatuses
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string NoFaces = "NoFaces";
    public const string Failed = "Failed";
}

public sealed class UploadRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; set; }

    [JsonPropertyName("containerName")]
    public required string ContainerName { get; init; }

    [JsonPropertyName("blobName")]
    public required string BlobName { get; init; }

    [JsonPropertyName("blobUrl")]
    public required string BlobUrl { get; init; }

    [JsonPropertyName("contentType")]
    public required string ContentType { get; init; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("detectedFaceCount")]
    public int? DetectedFaceCount { get; set; }

    [JsonPropertyName("failureSummary")]
    public string? FailureSummary { get; set; }
}