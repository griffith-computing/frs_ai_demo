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

using FaceLab.Core.Data;

namespace FaceLab.Maui.ViewModels;

/// <summary>An image staged for a run, already persisted to the local database.</summary>
public sealed class StagedImage
{
    public required ImageRecord Record { get; init; }

    public string FileName => Record.FileName;

    public string SizeText => $"{Record.ByteCount / 1024.0:N0} KB";

    public ImageSource Preview => ImageSource.FromStream(() => new MemoryStream(Record.Bytes));
}

/// <summary>Flattened view of one run for the results and history lists.</summary>
public sealed class RunSummary
{
    public required int RunId { get; init; }

    public required string FileName { get; init; }

    public required string Status { get; init; }

    public int DetectedFaceCount { get; init; }

    public long DurationMs { get; init; }

    public DateTimeOffset StartedUtc { get; init; }

    public string? ErrorMessage { get; init; }

    public string Headline =>
        $"#{RunId} {FileName} - {Status} - {DetectedFaceCount} face(s) in {DurationMs:N0} ms";

    public string Detail { get; init; } = string.Empty;

    public static RunSummary FromRun(RunRecord run, string fileName)
    {
        var faceLines = run.Faces.Select(f =>
        {
            var person = f.MatchedPersonId ?? f.EnrolledPersonId ?? "-";
            var confidence = f.Confidence > 0 ? $" confidence {f.Confidence:F3}" : string.Empty;
            var error = f.ErrorMessage is null ? string.Empty : $" [{f.ErrorMessage}]";
            return $"  {f.Outcome}: person {person}{confidence} rect {f.Left},{f.Top} {f.Width}x{f.Height}{error}";
        });

        var detail = string.Join(Environment.NewLine, faceLines);
        if (run.ErrorMessage is not null)
        {
            detail = string.IsNullOrEmpty(detail) ? run.ErrorMessage : detail + Environment.NewLine + run.ErrorMessage;
        }

        return new RunSummary
        {
            RunId = run.Id,
            FileName = fileName,
            Status = run.Status,
            DetectedFaceCount = run.DetectedFaceCount,
            DurationMs = run.DurationMs,
            StartedUtc = run.StartedUtc,
            ErrorMessage = run.ErrorMessage,
            Detail = detail
        };
    }
}
