//----------------------------------------------------------------------------------
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//----------------------------------------------------------------------------------

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceLab.Core.Tests;

public sealed class FaceEnrollmentQualityTests
{
    private static byte[] ImageBytes()
    {
        using var image = new Image<Rgba32>(300, 300);
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static DetectedFace EligibleFace() => new()
    {
        FaceId = "face-1",
        FaceRectangle = new FaceRectangle { Left = 20, Top = 20, Width = 120, Height = 120 },
        FaceAttributes = new FaceAttributes
        {
            QualityForRecognition = "high",
            Mask = new FaceMask { NoseAndMouthCovered = false },
            HeadPose = new HeadPose()
        }
    };

    [Fact]
    public void Evaluate_accepts_a_strict_high_quality_face()
    {
        var result = FaceEnrollmentQuality.Evaluate(EligibleFace(), ImageBytes(), TestOptions.Create());

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_rejects_a_partial_face_touching_an_image_edge()
    {
        var face = EligibleFace();
        face = new DetectedFace
        {
            FaceId = face.FaceId,
            FaceRectangle = new FaceRectangle { Left = 0, Top = 20, Width = 120, Height = 120 },
            FaceAttributes = face.FaceAttributes
        };

        var result = FaceEnrollmentQuality.Evaluate(face, ImageBytes(), TestOptions.Create());

        Assert.False(result.IsEligible);
        Assert.Contains("boundary", result.Reason);
    }

    [Fact]
    public void Evaluate_rejects_non_high_quality_masked_small_or_off_angle_faces()
    {
        var options = TestOptions.Create();
        var bytes = ImageBytes();

        var lowQuality = EligibleFace();
        lowQuality = new DetectedFace
        {
            FaceRectangle = lowQuality.FaceRectangle,
            FaceAttributes = new FaceAttributes
            {
                QualityForRecognition = "medium",
                Mask = lowQuality.FaceAttributes!.Mask,
                HeadPose = lowQuality.FaceAttributes.HeadPose
            }
        };
        Assert.False(FaceEnrollmentQuality.Evaluate(lowQuality, bytes, options).IsEligible);

        var masked = EligibleFace();
        masked = new DetectedFace
        {
            FaceRectangle = masked.FaceRectangle,
            FaceAttributes = new FaceAttributes
            {
                QualityForRecognition = "high",
                Mask = new FaceMask { NoseAndMouthCovered = true },
                HeadPose = new HeadPose()
            }
        };
        Assert.False(FaceEnrollmentQuality.Evaluate(masked, bytes, options).IsEligible);

        var small = EligibleFace();
        small = new DetectedFace
        {
            FaceRectangle = new FaceRectangle { Left = 20, Top = 20, Width = 99, Height = 120 },
            FaceAttributes = small.FaceAttributes
        };
        Assert.False(FaceEnrollmentQuality.Evaluate(small, bytes, options).IsEligible);

        var offAngle = EligibleFace();
        offAngle = new DetectedFace
        {
            FaceRectangle = offAngle.FaceRectangle,
            FaceAttributes = new FaceAttributes
            {
                QualityForRecognition = "high",
                Mask = new FaceMask { NoseAndMouthCovered = false },
                HeadPose = new HeadPose { Yaw = 15.1 }
            }
        };
        Assert.False(FaceEnrollmentQuality.Evaluate(offAngle, bytes, options).IsEligible);
    }
}
