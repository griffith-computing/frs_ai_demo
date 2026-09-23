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

using SixLabors.ImageSharp;

namespace FaceLab.Core;

public sealed record FaceEnrollmentQualityDecision(bool IsEligible, string Reason)
{
    public static FaceEnrollmentQualityDecision Eligible { get; } = new(true, "Eligible for automated enrollment.");
}

public static class FaceEnrollmentQuality
{
    public static FaceEnrollmentQualityDecision Evaluate(
        DetectedFace face,
        byte[] imageBytes,
        FaceLabOptions options)
    {
        var rectangle = face.FaceRectangle;
        if (rectangle is null)
        {
            return new(false, "Enrollment deferred because the Face API did not return a face rectangle.");
        }

        var attributes = face.FaceAttributes;
        if (!string.Equals(
                attributes?.QualityForRecognition,
                options.RequiredEnrollmentQuality,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                false,
                $"Enrollment deferred because recognition quality was {attributes?.QualityForRecognition ?? "not reported"}; " +
                $"{options.RequiredEnrollmentQuality} is required.");
        }

        if (attributes?.Mask?.NoseAndMouthCovered != false)
        {
            return new(false, "Enrollment deferred because the nose and mouth are covered or mask status was not reported.");
        }

        var pose = attributes.HeadPose;
        if (pose is null)
        {
            return new(false, "Enrollment deferred because head pose was not reported.");
        }

        if (Math.Abs(pose.Yaw) > options.MaximumEnrollmentYawDegrees ||
            Math.Abs(pose.Pitch) > options.MaximumEnrollmentPitchDegrees ||
            Math.Abs(pose.Roll) > options.MaximumEnrollmentRollDegrees)
        {
            return new(
                false,
                $"Enrollment deferred because head pose exceeded the configured limits " +
                $"(yaw {pose.Yaw:F1}, pitch {pose.Pitch:F1}, roll {pose.Roll:F1}).");
        }

        if (rectangle.Width < options.MinimumEnrollmentFaceSizePixels ||
            rectangle.Height < options.MinimumEnrollmentFaceSizePixels)
        {
            return new(
                false,
                $"Enrollment deferred because the face rectangle was {rectangle.Width}x{rectangle.Height}; " +
                $"at least {options.MinimumEnrollmentFaceSizePixels}x{options.MinimumEnrollmentFaceSizePixels} is required.");
        }

        ImageInfo? imageInfo;
        try
        {
            imageInfo = Image.Identify(imageBytes);
        }
        catch (UnknownImageFormatException)
        {
            return new(false, "Enrollment deferred because the image dimensions could not be read.");
        }
        if (imageInfo is null)
        {
            return new(false, "Enrollment deferred because the image dimensions could not be read.");
        }

        var margin = options.EnrollmentEdgeMarginPixels;
        if (rectangle.Left < margin ||
            rectangle.Top < margin ||
            rectangle.Left + rectangle.Width > imageInfo.Width - margin ||
            rectangle.Top + rectangle.Height > imageInfo.Height - margin)
        {
            return new(false, "Enrollment deferred because the detected face touches the image boundary and may be partial.");
        }

        return FaceEnrollmentQualityDecision.Eligible;
    }
}
