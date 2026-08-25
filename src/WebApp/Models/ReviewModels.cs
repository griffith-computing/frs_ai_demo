using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace FrsAiDemo.WebApp.Models;

public sealed class RecognitionEvent
{
    public DateTimeOffset TimestampUtc { get; init; }
    public required string BlobUrl { get; init; }
    public double Confidence { get; init; }
    public string? FaceId { get; init; }
}

public sealed class FaceRecord
{
    public required string Id { get; init; }
    public required string PersonId { get; init; }
    public required string PersonGroupId { get; init; }
    public DateTimeOffset FirstSeenUtc { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
    public List<RecognitionEvent> RecognitionHistory { get; init; } = new();
}

public sealed class UploadRecord
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required string ContainerName { get; init; }
    public required string BlobName { get; init; }
    public required string BlobUrl { get; init; }
    public required string ContentType { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public int? DetectedFaceCount { get; init; }
    public string? FailureSummary { get; init; }
}

public enum ReviewDecision
{
    Correct,
    Incorrect,
    Unsure
}

public sealed class ReviewRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("personId")]
    public required string PersonId { get; init; }

    [JsonPropertyName("sightingKey")]
    public required string SightingKey { get; init; }

    [JsonPropertyName("reviewerObjectId")]
    public required string ReviewerObjectId { get; init; }

    [JsonPropertyName("reviewerName")]
    public required string ReviewerName { get; set; }

    [JsonPropertyName("decision")]
    public required string Decision { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class ReviewInput
{
    [Required]
    public required string PersonId { get; init; }

    [Required]
    public required string SightingKey { get; init; }

    [Required]
    public ReviewDecision Decision { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }
}

public sealed record PageResult<T>(IReadOnlyList<T> Items, string? ContinuationToken);

public static class SightingKeys
{
    public static string Create(string personId, RecognitionEvent sighting)
    {
        var value = $"{personId}|{sighting.TimestampUtc:O}|{sighting.BlobUrl}|{sighting.FaceId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public static string CreateReviewId(string sightingKey, string reviewerObjectId)
    {
        var value = $"{sightingKey}|{reviewerObjectId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}