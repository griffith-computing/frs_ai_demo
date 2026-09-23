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
using FaceLab.Maui.Views;

namespace FaceLab.Maui.ViewModels;

/// <summary>Stages images in the local database and pushes them through the Face API pipeline.</summary>
public sealed partial class UploadViewModel : ObservableObject
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    private readonly IConfigStore _configStore;
    private readonly IFaceLabRepository _repository;
    private readonly IFaceRunner _runner;

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial string ConfigSummary { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public UploadViewModel(IConfigStore configStore, IFaceLabRepository repository, IFaceRunner runner)
    {
        _configStore = configStore;
        _repository = repository;
        _runner = runner;

        StatusMessage = "Pick one or more images, then run them against the current configuration.";
        ConfigSummary = string.Empty;
    }

    public ObservableCollection<StagedImage> Images { get; } = new();

    public ObservableCollection<RunSummary> Results { get; } = new();

    public bool CanRun => !IsBusy && Images.Count > 0;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRun));

    [RelayCommand]
    public async Task LoadAsync()
    {
        var options = await _configStore.LoadAsync();
        ConfigSummary = DescribeConfig(options);
    }

    [RelayCommand]
    private async Task PickImagesAsync()
    {
        try
        {
            var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Select images to send to the Face API",
                FileTypes = FilePickerFileType.Images
            });

            if (results is null)
            {
                return;
            }

            var options = await _configStore.LoadAsync();
            var added = 0;
            var skipped = new List<string>();

            foreach (var result in results)
            {
                if (result is null)
                {
                    continue;
                }

                var extension = Path.GetExtension(result.FileName) ?? string.Empty;
                if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    skipped.Add($"{result.FileName} (unsupported type)");
                    continue;
                }

                await using var stream = await result.OpenReadAsync();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                var bytes = buffer.ToArray();

                if (bytes.Length > options.MaxImageSizeBytes)
                {
                    skipped.Add($"{result.FileName} ({bytes.Length / 1024.0 / 1024.0:F1} MB exceeds the configured limit)");
                    continue;
                }

                var record = await _repository.SaveImageAsync(
                    result.FileName,
                    result.ContentType ?? "application/octet-stream",
                    bytes,
                    CancellationToken.None);

                if (Images.Any(i => i.Record.Id == record.Id))
                {
                    continue;
                }

                Images.Add(new StagedImage { Record = record });
                added++;
            }

            OnPropertyChanged(nameof(CanRun));
            StatusMessage = skipped.Count == 0
                ? $"Staged {added} image(s) in the local database."
                : $"Staged {added} image(s). Skipped: {string.Join("; ", skipped)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not read the selected files: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearImages()
    {
        Images.Clear();
        OnPropertyChanged(nameof(CanRun));
        StatusMessage = "Cleared the staged images. They remain in the local database.";
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Images.Count == 0)
        {
            StatusMessage = "Stage at least one image first.";
            return;
        }

        var options = await _configStore.LoadAsync();
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            StatusMessage = "Fix the configuration first: " + string.Join(" ", errors);
            return;
        }

        IsBusy = true;
        Results.Clear();
        ConfigSummary = DescribeConfig(options);

        try
        {
            // Snapshot the configuration so edits made mid-batch cannot change it under us.
            var runOptions = options.Clone();
            var index = 0;

            foreach (var staged in Images.ToList())
            {
                index++;
                StatusMessage = $"Running {index} of {Images.Count}: {staged.FileName}...";

                try
                {
                    var run = await _runner.RunAsync(staged.Record, runOptions, CancellationToken.None);
                    Results.Add(RunSummary.FromRun(run, staged.FileName));
                }
                catch (Exception ex)
                {
                    Results.Add(new RunSummary
                    {
                        RunId = 0,
                        FileName = staged.FileName,
                        Status = RunStatuses.Failed,
                        DetectedFaceCount = 0,
                        DurationMs = 0,
                        StartedUtc = DateTimeOffset.UtcNow,
                        ErrorMessage = ex.Message,
                        Detail = ex.Message
                    });
                }
            }

            var failed = Results.Count(r => r.Status == RunStatuses.Failed);
            StatusMessage = failed == 0
                ? $"Completed {Results.Count} run(s). Open History for the raw Face API calls."
                : $"Completed {Results.Count} run(s), {failed} failed. Open History for the raw Face API calls.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenRunAsync(RunSummary? summary)
    {
        if (summary is null || summary.RunId == 0)
        {
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(RunDetailPage)}?runId={summary.RunId}");
    }

    private static string DescribeConfig(FaceLabOptions options) =>
        $"{(string.IsNullOrWhiteSpace(options.Endpoint) ? "(no endpoint set)" : options.Endpoint)} | " +
        $"{options.ApiVersionSegment} | {options.DetectionModel} / {options.RecognitionModel} | " +
        $"group {options.DynamicPersonGroupId} | threshold {options.ConfidenceThreshold:F2} | " +
        $"auth {options.AuthMode} | provisional enrollment {(options.AutoEnrollUnmatchedFaces ? "on" : "off")} | " +
        $"{options.RequiredEnrollmentImages} images | verify {options.ProvisionalVerificationThreshold:F2}";
}
