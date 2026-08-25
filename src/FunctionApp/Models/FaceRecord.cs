using System.Text.Json.Serialization;

namespace FrsAiDemo.FunctionApp.Models;

/// <summary>
/// A single recognition event: one occurrence of a known face being seen in an uploaded photo.
/// </summary>
public sealed class RecognitionEvent
{
    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("blobUrl")]
    public required string BlobUrl { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("faceId")]
    public string? FaceId { get; init; }
}

/// <summary>
/// Cosmos DB document representing a person recognized by the Face API person directory.
/// Partitioned by /personId. Created on first sighting, updated (lastSeenUtc + history) on
/// every subsequent recognition of the same person.
/// </summary>
public sealed class FaceRecord
{
    /// <summary>Cosmos document id - same value as PersonId.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Azure AI Face API Person Directory person id. Also the partition key value.</summary>
    [JsonPropertyName("personId")]
    public required string PersonId { get; init; }

    [JsonPropertyName("personGroupId")]
    // The JSON property name is retained so records created by the legacy PersonGroup flow stay readable.
    public required string PersonGroupId { get; init; }

    [JsonPropertyName("firstSeenUtc")]
    public DateTimeOffset FirstSeenUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("lastSeenUtc")]
    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("recognitionHistory")]
    public List<RecognitionEvent> RecognitionHistory { get; init; } = new();
}
