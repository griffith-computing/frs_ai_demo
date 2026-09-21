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

namespace FaceLab.Core;

/// <summary>
/// One captured Face API HTTP call. The whole point of the test bench: see exactly what was
/// sent, what came back, and how long it took for the configuration under test.
/// </summary>
public sealed class CallTrace
{
    public const int MaxBodyLength = 8000;

    public required string Operation { get; init; }

    public required string Method { get; init; }

    public required string Url { get; init; }

    public int? StatusCode { get; set; }

    public long ElapsedMilliseconds { get; set; }

    public string? RequestSummary { get; init; }

    public string? ResponseBody { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsSuccess => Error is null && StatusCode is >= 200 and < 300;

    public static string? Truncate(string? body)
    {
        if (body is null || body.Length <= MaxBodyLength)
        {
            return body;
        }

        return body[..MaxBodyLength] + $"... [truncated, {body.Length} chars total]";
    }
}
