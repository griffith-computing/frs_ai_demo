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
