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

/// <summary>A face detected within an uploaded photo via the Face API "Detect" operation.</summary>
public sealed class DetectedFace
{
    [JsonPropertyName("faceId")]
    public string? FaceId { get; init; }

    [JsonPropertyName("faceRectangle")]
    public FaceRectangle? FaceRectangle { get; init; }
}

public sealed class FaceRectangle
{
    [JsonPropertyName("top")]
    public int Top { get; init; }

    [JsonPropertyName("left")]
    public int Left { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }
}

/// <summary>A single identify result: candidate matches for one detected faceId.</summary>
public sealed class IdentifyResult
{
    [JsonPropertyName("faceId")]
    public string? FaceId { get; init; }

    [JsonPropertyName("candidates")]
    public List<IdentifyCandidate> Candidates { get; init; } = new();
}

public sealed class IdentifyCandidate
{
    [JsonPropertyName("personId")]
    public string? PersonId { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}

public sealed class FaceOperationResult
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed class CreatePersonResponse
{
    [JsonPropertyName("personId")]
    public string? PersonId { get; init; }
}
