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

    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = "upload";

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

    [JsonPropertyName("processingLeaseUntilUtc")]
    public DateTimeOffset? ProcessingLeaseUntilUtc { get; set; }

    [JsonPropertyName("processingLeaseOwner")]
    public string? ProcessingLeaseOwner { get; set; }

    [JsonPropertyName("batchId")]
    public string? BatchId { get; init; }

    [JsonPropertyName("importId")]
    public string? ImportId { get; init; }

    [JsonPropertyName("originalFileName")]
    public string? OriginalFileName { get; init; }

    [JsonPropertyName("sourceAccountName")]
    public string? SourceAccountName { get; init; }
}

public sealed class UploadBatchRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = "batch";
    [JsonPropertyName("importId")]
    public string? ImportId { get; init; }
    [JsonPropertyName("ownerObjectId")]
    public required string OwnerObjectId { get; init; }
    [JsonPropertyName("expectedFileCount")]
    public int ExpectedFileCount { get; init; }
    [JsonPropertyName("submittedFileCount")]
    public int SubmittedFileCount { get; init; }
    [JsonPropertyName("submissionCompleted")]
    public bool SubmissionCompleted { get; set; }
    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; }
    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class StorageImportRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = "import";
    [JsonPropertyName("ownerObjectId")]
    public required string OwnerObjectId { get; init; }
    [JsonPropertyName("sourceKey")]
    public required string SourceKey { get; init; }
    [JsonPropertyName("sourceAccountName")]
    public required string SourceAccountName { get; init; }
    [JsonPropertyName("sourceContainerName")]
    public required string SourceContainerName { get; init; }
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Pending";
    [JsonPropertyName("continuationToken")]
    public string? ContinuationToken { get; set; }
    [JsonPropertyName("nextBatchNumber")]
    public int NextBatchNumber { get; set; }
    [JsonPropertyName("discoveredFileCount")]
    public int DiscoveredFileCount { get; set; }
    [JsonPropertyName("consecutiveFailureCount")]
    public int ConsecutiveFailureCount { get; set; }
    [JsonPropertyName("failureSummary")]
    public string? FailureSummary { get; set; }
    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; }
    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; set; }
}