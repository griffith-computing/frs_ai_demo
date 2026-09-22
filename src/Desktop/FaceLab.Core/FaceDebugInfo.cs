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

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaceLab.Core;

/// <summary>
/// Everything captured about one detected face beyond the rectangle/faceId: recognition model,
/// head pose, mask, quality for recognition, and every face landmark. Serialized onto
/// <see cref="Data.FaceResultRecord.DebugInfoJson"/> so it survives reloads of a past run.
/// </summary>
public sealed class FaceDebugInfo
{
    [JsonPropertyName("recognitionModel")]
    public string? RecognitionModel { get; init; }

    [JsonPropertyName("faceAttributes")]
    public FaceAttributes? FaceAttributes { get; init; }

    [JsonPropertyName("faceLandmarks")]
    public FaceLandmarks? FaceLandmarks { get; init; }

    /// <summary>Builds the debug info to persist for one detected face, or null if the Face API returned none.</summary>
    public static FaceDebugInfo? FromDetectedFace(DetectedFace face)
    {
        if (face.FaceAttributes is null && face.FaceLandmarks is null && face.RecognitionModel is null)
        {
            return null;
        }

        return new FaceDebugInfo
        {
            RecognitionModel = face.RecognitionModel,
            FaceAttributes = face.FaceAttributes,
            FaceLandmarks = face.FaceLandmarks
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static FaceDebugInfo? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<FaceDebugInfo>(json);
}

/// <summary>Formats <see cref="FaceDebugInfo"/> (plus the face rectangle) into readable, dialog-friendly text.</summary>
public static class FaceDebugInfoFormatter
{
    /// <summary>Formats the full debug payload for a face into a multi-line string for display.</summary>
    public static string Format(FaceRectangle rectangle, FaceDebugInfo? debugInfo)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Face Rectangle: left={rectangle.Left}, top={rectangle.Top}, width={rectangle.Width}, height={rectangle.Height}");

        if (debugInfo is null)
        {
            sb.Append("No additional debug information was returned by the Face API.");
            return sb.ToString();
        }

        var headPose = debugInfo.FaceAttributes?.HeadPose;
        if (headPose is not null)
        {
            sb.AppendLine($"Head pose: pitch={headPose.Pitch}, roll={headPose.Roll}, yaw={headPose.Yaw}");
        }

        var mask = debugInfo.FaceAttributes?.Mask;
        if (mask is not null)
        {
            sb.AppendLine($"Mask: NoseAndMouthCovered={mask.NoseAndMouthCovered}, Type={mask.Type}");
        }

        if (debugInfo.FaceAttributes?.QualityForRecognition is not null)
        {
            sb.AppendLine($"Quality: {debugInfo.FaceAttributes.QualityForRecognition}");
        }

        if (debugInfo.RecognitionModel is not null)
        {
            sb.AppendLine($"Recognition model: {debugInfo.RecognitionModel}");
        }

        var landmarks = debugInfo.FaceLandmarks;
        if (landmarks is not null)
        {
            sb.AppendLine("Landmarks:");
            AppendLandmark(sb, "PupilLeft", landmarks.PupilLeft);
            AppendLandmark(sb, "PupilRight", landmarks.PupilRight);
            AppendLandmark(sb, "NoseTip", landmarks.NoseTip);
            AppendLandmark(sb, "MouthLeft", landmarks.MouthLeft);
            AppendLandmark(sb, "MouthRight", landmarks.MouthRight);
            AppendLandmark(sb, "EyebrowLeftOuter", landmarks.EyebrowLeftOuter);
            AppendLandmark(sb, "EyebrowLeftInner", landmarks.EyebrowLeftInner);
            AppendLandmark(sb, "EyeLeftOuter", landmarks.EyeLeftOuter);
            AppendLandmark(sb, "EyeLeftTop", landmarks.EyeLeftTop);
            AppendLandmark(sb, "EyeLeftBottom", landmarks.EyeLeftBottom);
            AppendLandmark(sb, "EyeLeftInner", landmarks.EyeLeftInner);
            AppendLandmark(sb, "EyebrowRightInner", landmarks.EyebrowRightInner);
            AppendLandmark(sb, "EyebrowRightOuter", landmarks.EyebrowRightOuter);
            AppendLandmark(sb, "EyeRightInner", landmarks.EyeRightInner);
            AppendLandmark(sb, "EyeRightTop", landmarks.EyeRightTop);
            AppendLandmark(sb, "EyeRightBottom", landmarks.EyeRightBottom);
            AppendLandmark(sb, "EyeRightOuter", landmarks.EyeRightOuter);
            AppendLandmark(sb, "NoseRootLeft", landmarks.NoseRootLeft);
            AppendLandmark(sb, "NoseRootRight", landmarks.NoseRootRight);
            AppendLandmark(sb, "NoseLeftAlarTop", landmarks.NoseLeftAlarTop);
            AppendLandmark(sb, "NoseRightAlarTop", landmarks.NoseRightAlarTop);
            AppendLandmark(sb, "NoseLeftAlarOutTip", landmarks.NoseLeftAlarOutTip);
            AppendLandmark(sb, "NoseRightAlarOutTip", landmarks.NoseRightAlarOutTip);
            AppendLandmark(sb, "UpperLipTop", landmarks.UpperLipTop);
            AppendLandmark(sb, "UpperLipBottom", landmarks.UpperLipBottom);
            AppendLandmark(sb, "UnderLipTop", landmarks.UnderLipTop);
            AppendLandmark(sb, "UnderLipBottom", landmarks.UnderLipBottom);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendLandmark(StringBuilder sb, string name, LandmarkPoint? point)
    {
        if (point is not null)
        {
            sb.AppendLine($"    {name}: ({point.X}, {point.Y})");
        }
    }
}
