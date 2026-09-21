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
using FaceLab.Core.Data;
using FaceLab.Maui.Views;

namespace FaceLab.Maui.ViewModels;

/// <summary>Lists past runs so configurations can be compared after the fact.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private const int MaxRuns = 200;

    private readonly IFaceLabRepository _repository;

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public HistoryViewModel(IFaceLabRepository repository)
    {
        _repository = repository;
        StatusMessage = string.Empty;
    }

    public ObservableCollection<RunSummary> Runs { get; } = new();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var runs = await _repository.GetRunsAsync(MaxRuns, CancellationToken.None);
            var images = await _repository.GetImagesAsync(MaxRuns, CancellationToken.None);
            var fileNamesById = images.ToDictionary(i => i.Id, i => i.FileName);

            Runs.Clear();
            foreach (var run in runs)
            {
                Runs.Add(RunSummary.FromRun(run, fileNamesById.GetValueOrDefault(run.ImageId, $"image #{run.ImageId}")));
            }

            StatusMessage = Runs.Count == 0
                ? "No runs yet. Upload an image to get started."
                : $"{Runs.Count} run(s) stored locally.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenRunAsync(RunSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(RunDetailPage)}?runId={summary.RunId}");
    }
}
