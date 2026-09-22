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

namespace FaceLab.Core.Tests;

public sealed class FaceDebugInfoTests
{
    private static DetectedFace FullFace() => new()
    {
        FaceId = "face-1",
        FaceRectangle = new FaceRectangle { Top = 1, Left = 2, Width = 3, Height = 4 },
        RecognitionModel = "recognition_04",
        FaceAttributes = new FaceAttributes
        {
            HeadPose = new HeadPose { Pitch = 1.1, Roll = 2.2, Yaw = 3.3 },
            Mask = new FaceMask { NoseAndMouthCovered = true, Type = "faceMask" },
            QualityForRecognition = "high"
        },
        FaceLandmarks = new FaceLandmarks
        {
            PupilLeft = new LandmarkPoint { X = 10, Y = 20 },
            PupilRight = new LandmarkPoint { X = 30, Y = 40 }
        }
    };

    [Fact]
    public void FromDetectedFace_returns_null_when_no_debug_data_was_returned()
    {
        var face = new DetectedFace { FaceId = "face-1", FaceRectangle = new FaceRectangle() };

        Assert.Null(FaceDebugInfo.FromDetectedFace(face));
    }

    [Fact]
    public void FromDetectedFace_round_trips_through_json()
    {
        var debugInfo = FaceDebugInfo.FromDetectedFace(FullFace());
        Assert.NotNull(debugInfo);

        var json = debugInfo!.ToJson();
        var reloaded = FaceDebugInfo.FromJson(json);

        Assert.NotNull(reloaded);
        Assert.Equal("recognition_04", reloaded!.RecognitionModel);
        Assert.Equal(1.1, reloaded.FaceAttributes!.HeadPose!.Pitch);
        Assert.Equal(10, reloaded.FaceLandmarks!.PupilLeft!.X);
    }

    [Fact]
    public void Format_includes_rectangle_head_pose_mask_quality_model_and_landmarks()
    {
        var face = FullFace();
        var debugInfo = FaceDebugInfo.FromDetectedFace(face);

        var text = FaceDebugInfoFormatter.Format(face.FaceRectangle!, debugInfo);

        Assert.Contains("Face Rectangle: left=2, top=1, width=3, height=4", text);
        Assert.Contains("Head pose: pitch=1.1, roll=2.2, yaw=3.3", text);
        Assert.Contains("Mask: NoseAndMouthCovered=True, Type=faceMask", text);
        Assert.Contains("Quality: high", text);
        Assert.Contains("Recognition model: recognition_04", text);
        Assert.Contains("PupilLeft: (10, 20)", text);
        Assert.Contains("PupilRight: (30, 40)", text);
    }

    [Fact]
    public void Format_handles_a_missing_debug_payload()
    {
        var text = FaceDebugInfoFormatter.Format(new FaceRectangle { Top = 1, Left = 2, Width = 3, Height = 4 }, null);

        Assert.Contains("Face Rectangle: left=2, top=1, width=3, height=4", text);
        Assert.Contains("No additional debug information was returned", text);
    }
}
