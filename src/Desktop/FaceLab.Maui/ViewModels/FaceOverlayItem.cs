using CommunityToolkit.Mvvm.ComponentModel;
using FaceLab.Core;
using FaceLab.Core.Data;

namespace FaceLab.Maui.ViewModels;

public sealed partial class FaceOverlayItem : ObservableObject
{
    public FaceResultRecord Record { get; }

    public string PersonLabel =>
        Record.MatchedPersonId is not null
            ? $"Matched person: {Record.MatchedPersonId}"
            : Record.EnrolledPersonId is not null
                ? $"Enrolled person: {Record.EnrolledPersonId}"
                : $"Face: {Record.FaceId ?? "unknown"}";

    public IReadOnlyList<LandmarkPoint> Landmarks { get; }

    public bool HasLandmarks => Landmarks.Count > 0;

    [ObservableProperty]
    public partial bool ShowRectangle { get; set; }

    [ObservableProperty]
    public partial bool ShowLandmarks { get; set; }

    public FaceOverlayItem(FaceResultRecord record)
    {
        Record = record;
        ShowRectangle = record.Width > 0 && record.Height > 0;
        Landmarks = GetLandmarks(record.DebugInfoJson);
    }

    private static IReadOnlyList<LandmarkPoint> GetLandmarks(string? json)
    {
        var landmarks = FaceDebugInfo.FromJson(json)?.FaceLandmarks;
        if (landmarks is null)
        {
            return Array.Empty<LandmarkPoint>();
        }

        var points = new LandmarkPoint?[]
        {
            landmarks.PupilLeft, landmarks.PupilRight, landmarks.NoseTip,
            landmarks.MouthLeft, landmarks.MouthRight,
            landmarks.EyebrowLeftOuter, landmarks.EyebrowLeftInner,
            landmarks.EyeLeftOuter, landmarks.EyeLeftTop, landmarks.EyeLeftBottom, landmarks.EyeLeftInner,
            landmarks.EyebrowRightInner, landmarks.EyebrowRightOuter,
            landmarks.EyeRightInner, landmarks.EyeRightTop, landmarks.EyeRightBottom, landmarks.EyeRightOuter,
            landmarks.NoseRootLeft, landmarks.NoseRootRight,
            landmarks.NoseLeftAlarTop, landmarks.NoseRightAlarTop,
            landmarks.NoseLeftAlarOutTip, landmarks.NoseRightAlarOutTip,
            landmarks.UpperLipTop, landmarks.UpperLipBottom,
            landmarks.UnderLipTop, landmarks.UnderLipBottom
        };
        return points.Where(point => point is not null).Select(point => point!).ToList();
    }
}
