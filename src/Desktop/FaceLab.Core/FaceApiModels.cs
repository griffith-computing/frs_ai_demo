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

namespace FaceLab.Core;

/// <summary>A face detected within an uploaded photo via the Face API "Detect" operation.</summary>
public sealed class DetectedFace
{
    [JsonPropertyName("faceId")]
    public string? FaceId { get; init; }

    [JsonPropertyName("faceRectangle")]
    public FaceRectangle? FaceRectangle { get; init; }

    [JsonPropertyName("faceAttributes")]
    public FaceAttributes? FaceAttributes { get; init; }

    [JsonPropertyName("faceLandmarks")]
    public FaceLandmarks? FaceLandmarks { get; init; }

    [JsonPropertyName("recognitionModel")]
    public string? RecognitionModel { get; init; }
}

/// <summary>Detection-time attributes requested alongside Detect: head pose, mask and recognition quality.</summary>
public sealed class FaceAttributes
{
    [JsonPropertyName("headPose")]
    public HeadPose? HeadPose { get; init; }

    [JsonPropertyName("mask")]
    public FaceMask? Mask { get; init; }

    [JsonPropertyName("qualityForRecognition")]
    public string? QualityForRecognition { get; init; }
}

public sealed class HeadPose
{
    [JsonPropertyName("pitch")]
    public double Pitch { get; init; }

    [JsonPropertyName("roll")]
    public double Roll { get; init; }

    [JsonPropertyName("yaw")]
    public double Yaw { get; init; }
}

public sealed class FaceMask
{
    [JsonPropertyName("noseAndMouthCovered")]
    public bool NoseAndMouthCovered { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

/// <summary>A single (x, y) landmark coordinate, in pixels.</summary>
public sealed class LandmarkPoint
{
    [JsonPropertyName("x")]
    public double X { get; init; }

    [JsonPropertyName("y")]
    public double Y { get; init; }
}

/// <summary>The 27 facial landmark points returned when returnFaceLandmarks=true.</summary>
public sealed class FaceLandmarks
{
    [JsonPropertyName("pupilLeft")]
    public LandmarkPoint? PupilLeft { get; init; }

    [JsonPropertyName("pupilRight")]
    public LandmarkPoint? PupilRight { get; init; }

    [JsonPropertyName("noseTip")]
    public LandmarkPoint? NoseTip { get; init; }

    [JsonPropertyName("mouthLeft")]
    public LandmarkPoint? MouthLeft { get; init; }

    [JsonPropertyName("mouthRight")]
    public LandmarkPoint? MouthRight { get; init; }

    [JsonPropertyName("eyebrowLeftOuter")]
    public LandmarkPoint? EyebrowLeftOuter { get; init; }

    [JsonPropertyName("eyebrowLeftInner")]
    public LandmarkPoint? EyebrowLeftInner { get; init; }

    [JsonPropertyName("eyeLeftOuter")]
    public LandmarkPoint? EyeLeftOuter { get; init; }

    [JsonPropertyName("eyeLeftTop")]
    public LandmarkPoint? EyeLeftTop { get; init; }

    [JsonPropertyName("eyeLeftBottom")]
    public LandmarkPoint? EyeLeftBottom { get; init; }

    [JsonPropertyName("eyeLeftInner")]
    public LandmarkPoint? EyeLeftInner { get; init; }

    [JsonPropertyName("eyebrowRightInner")]
    public LandmarkPoint? EyebrowRightInner { get; init; }

    [JsonPropertyName("eyebrowRightOuter")]
    public LandmarkPoint? EyebrowRightOuter { get; init; }

    [JsonPropertyName("eyeRightInner")]
    public LandmarkPoint? EyeRightInner { get; init; }

    [JsonPropertyName("eyeRightTop")]
    public LandmarkPoint? EyeRightTop { get; init; }

    [JsonPropertyName("eyeRightBottom")]
    public LandmarkPoint? EyeRightBottom { get; init; }

    [JsonPropertyName("eyeRightOuter")]
    public LandmarkPoint? EyeRightOuter { get; init; }

    [JsonPropertyName("noseRootLeft")]
    public LandmarkPoint? NoseRootLeft { get; init; }

    [JsonPropertyName("noseRootRight")]
    public LandmarkPoint? NoseRootRight { get; init; }

    [JsonPropertyName("noseLeftAlarTop")]
    public LandmarkPoint? NoseLeftAlarTop { get; init; }

    [JsonPropertyName("noseRightAlarTop")]
    public LandmarkPoint? NoseRightAlarTop { get; init; }

    [JsonPropertyName("noseLeftAlarOutTip")]
    public LandmarkPoint? NoseLeftAlarOutTip { get; init; }

    [JsonPropertyName("noseRightAlarOutTip")]
    public LandmarkPoint? NoseRightAlarOutTip { get; init; }

    [JsonPropertyName("upperLipTop")]
    public LandmarkPoint? UpperLipTop { get; init; }

    [JsonPropertyName("upperLipBottom")]
    public LandmarkPoint? UpperLipBottom { get; init; }

    [JsonPropertyName("underLipTop")]
    public LandmarkPoint? UnderLipTop { get; init; }

    [JsonPropertyName("underLipBottom")]
    public LandmarkPoint? UnderLipBottom { get; init; }
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
