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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceLab.Core;
using FaceLab.Core.Data;
using FaceLab.Maui.Services;

namespace FaceLab.Maui.ViewModels;

/// <summary>
/// Everything recorded for a single run: the image, the per-face outcomes, the exact configuration
/// used, and every raw Face API request/response captured while it ran.
/// </summary>
public sealed partial class RunDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly IFaceLabRepository _repository;
    private readonly IFaceRunner _runner;
    private readonly IConfigStore _configStore;

    [ObservableProperty]
    public partial int RunId { get; set; }

    [ObservableProperty]
    public partial RunRecord? Run { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string ConfigSnapshot { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public RunDetailViewModel(IFaceLabRepository repository, IFaceRunner runner, IConfigStore configStore)
    {
        _repository = repository;
        _runner = runner;
        _configStore = configStore;

        Headline = string.Empty;
        ConfigSnapshot = string.Empty;
        StatusMessage = string.Empty;
    }

    public ObservableCollection<FaceResultRecord> Faces { get; } = new();

    public ObservableCollection<FaceOverlayItem> FaceOverlays { get; } = new();

    public ObservableCollection<CallTraceRecord> Traces { get; } = new();

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("runId", out var value) &&
            int.TryParse(value?.ToString(), out var parsed))
        {
            RunId = parsed;
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (RunId <= 0)
        {
            return;
        }

        var record = await _repository.GetRunAsync(RunId, CancellationToken.None);
        if (record is null)
        {
            StatusMessage = $"Run #{RunId} was not found in the local database.";
            return;
        }

        Run = record;
        Headline = $"Run #{record.Id} - {record.Status} - {record.DetectedFaceCount} face(s) in {record.DurationMs:N0} ms " +
                   $"({record.StartedUtc.ToLocalTime():g})";
        ConfigSnapshot = record.ConfigSnapshotJson;
        StatusMessage = record.ErrorMessage ?? string.Empty;

        Faces.Clear();
        FaceOverlays.Clear();
        foreach (var face in record.Faces)
        {
            Faces.Add(face);
            FaceOverlays.Add(new FaceOverlayItem(face));
        }

        Traces.Clear();
        foreach (var trace in record.Traces.OrderBy(t => t.TimestampUtc).ThenBy(t => t.Id))
        {
            Traces.Add(trace);
        }

        if (record.Image is not null)
        {
            var bytes = record.Image.Bytes;
            Preview = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
    }

    /// <summary>Shows a dialog with the full detect-time debug data (head pose, mask, quality, landmarks) for one face.</summary>
    [RelayCommand]
    private async Task ShowDebugInfoAsync(FaceResultRecord? face)
    {
        if (face is null)
        {
            return;
        }

        var rectangle = new FaceRectangle { Top = face.Top, Left = face.Left, Width = face.Width, Height = face.Height };
        var debugInfo = FaceDebugInfo.FromJson(face.DebugInfoJson);
        var message = FaceDebugInfoFormatter.Format(rectangle, debugInfo);

        await Shell.Current.DisplayAlertAsync($"Debug info - {face.FaceId ?? "face"}", message, "Close");
    }

    /// <summary>Re-runs the same image against the configuration currently being edited.</summary>
    [RelayCommand]
    private async Task RerunAsync()
    {
        if (Run?.Image is null)
        {
            StatusMessage = "The original image is no longer available.";
            return;
        }

        IsBusy = true;
        try
        {
            var options = await _configStore.LoadAsync();
            var errors = options.Validate();
            if (errors.Count > 0)
            {
                StatusMessage = "Fix the configuration first: " + string.Join(" ", errors);
                return;
            }

            var newRun = await _runner.RunAsync(Run.Image, options.Clone(), CancellationToken.None);
            RunId = newRun.Id;
            await LoadAsync();
            StatusMessage = $"Re-ran the image with the current configuration as run #{newRun.Id}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Re-run failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
