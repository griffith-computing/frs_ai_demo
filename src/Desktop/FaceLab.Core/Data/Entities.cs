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

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FaceLab.Core.Data;

/// <summary>An image imported into the local test bench. Bytes live in SQLite, not on disk.</summary>
public sealed class ImageRecord
{
    public int Id { get; set; }

    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Hex SHA-256 of the bytes; re-importing the same file reuses the existing row.</summary>
    [MaxLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    public int ByteCount { get; set; }

    public byte[] Bytes { get; set; } = Array.Empty<byte>();

    public DateTimeOffset ImportedUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<RunRecord> Runs { get; set; } = new();
}

public static class RunStatuses
{
    public const string Completed = "Completed";
    public const string NoFaces = "NoFaces";
    public const string Failed = "Failed";
}

/// <summary>One execution of the Detect/Identify/enroll pipeline against one image.</summary>
public sealed class RunRecord
{
    public int Id { get; set; }

    public int ImageId { get; set; }

    public ImageRecord? Image { get; set; }

    /// <summary>The full FaceLabOptions JSON (minus secrets) used for this run.</summary>
    public string ConfigSnapshotJson { get; set; } = string.Empty;

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;

    public long DurationMs { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = RunStatuses.Completed;

    public int DetectedFaceCount { get; set; }

    public string? ErrorMessage { get; set; }

    public List<FaceResultRecord> Faces { get; set; } = new();

    public List<CallTraceRecord> Traces { get; set; } = new();

    [NotMapped]
    public FaceLabOptions Config => FaceLabOptions.FromSnapshotJson(ConfigSnapshotJson);
}

public static class FaceOutcomes
{
    public const string Matched = "Matched";
    public const string Enrolled = "Enrolled";
    public const string NoMatch = "NoMatch";
    public const string Failed = "Failed";
}

public sealed class FaceResultRecord
{
    public int Id { get; set; }

    public int RunId { get; set; }

    public RunRecord? Run { get; set; }

    [MaxLength(100)]
    public string? FaceId { get; set; }

    public int Top { get; set; }

    public int Left { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    [MaxLength(32)]
    public string Outcome { get; set; } = FaceOutcomes.NoMatch;

    [MaxLength(100)]
    public string? MatchedPersonId { get; set; }

    public double Confidence { get; set; }

    [MaxLength(100)]
    public string? EnrolledPersonId { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class CallTraceRecord
{
    public int Id { get; set; }

    public int RunId { get; set; }

    public RunRecord? Run { get; set; }

    [MaxLength(100)]
    public string Operation { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Method { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public int? StatusCode { get; set; }

    public long ElapsedMs { get; set; }

    public string? RequestSummary { get; set; }

    public string? ResponseBody { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    public static CallTraceRecord FromTrace(CallTrace trace) => new()
    {
        Operation = trace.Operation,
        Method = trace.Method,
        Url = trace.Url,
        StatusCode = trace.StatusCode,
        ElapsedMs = trace.ElapsedMilliseconds,
        RequestSummary = trace.RequestSummary,
        ResponseBody = trace.ResponseBody,
        Error = trace.Error,
        TimestampUtc = trace.TimestampUtc
    };
}

/// <summary>Local analog of the Cosmos "Faces" container: one row per person seen locally.</summary>
public sealed class PersonRecord
{
    [MaxLength(100)]
    public string PersonId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string DynamicPersonGroupId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? DisplayName { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;

    public int SightingCount { get; set; }

    public double LastConfidence { get; set; }
}

/// <summary>A named, reusable Face API configuration. Never contains the subscription key.</summary>
public sealed class ConfigProfile
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string Json { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
