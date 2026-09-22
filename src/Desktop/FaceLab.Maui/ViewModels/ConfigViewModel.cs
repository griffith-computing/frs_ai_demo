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

/// <summary>Edits the Face API configuration under test, manages named profiles, and smoke-tests the endpoint.</summary>
public sealed partial class ConfigViewModel : ObservableObject
{
    private readonly IConfigStore _configStore;
    private readonly IFaceLabRepository _repository;
    private readonly IFaceRunner _runner;

    [ObservableProperty]
    public partial FaceLabOptions Options { get; set; }

    [ObservableProperty]
    public partial string SelectedAuthMode { get; set; }

    [ObservableProperty]
    public partial string ProfileName { get; set; }

    [ObservableProperty]
    public partial ConfigProfile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial string TraceDetail { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ConfigViewModel(IConfigStore configStore, IFaceLabRepository repository, IFaceRunner runner)
    {
        _configStore = configStore;
        _repository = repository;
        _runner = runner;

        Options = new FaceLabOptions();
        SelectedAuthMode = nameof(FaceAuthMode.SubscriptionKey);
        ProfileName = string.Empty;
        StatusMessage = string.Empty;
        TraceDetail = string.Empty;
    }

    public IReadOnlyList<string> AuthModes { get; } = new[]
    {
        nameof(FaceAuthMode.SubscriptionKey),
        nameof(FaceAuthMode.DefaultAzureCredential)
    };

    public ObservableCollection<ConfigProfile> Profiles { get; } = new();

    public bool IsKeyAuth => SelectedAuthMode == nameof(FaceAuthMode.SubscriptionKey);

    public string DatabasePath => Path.Combine(FileSystem.AppDataDirectory, MauiProgram.DatabaseFileName);

    partial void OnSelectedAuthModeChanged(string value)
    {
        Options.AuthMode = Enum.TryParse<FaceAuthMode>(value, out var mode) ? mode : FaceAuthMode.SubscriptionKey;
        OnPropertyChanged(nameof(IsKeyAuth));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Options = await _configStore.LoadAsync();
        SelectedAuthMode = Options.AuthMode.ToString();
        await RefreshProfilesAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var errors = Options.Validate();
        if (errors.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, errors);
            return;
        }

        await _configStore.SaveAsync(Options);
        StatusMessage = $"Configuration saved at {DateTime.Now:T}. Runs will use it immediately.";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var key = Options.SubscriptionKey;
        Options = new FaceLabOptions { SubscriptionKey = key };
        SelectedAuthMode = Options.AuthMode.ToString();
        _configStore.Replace(Options);
        StatusMessage = "Reverted to the default Face API settings (endpoint and key preserved).";
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            StatusMessage = "Enter a profile name before saving.";
            return;
        }

        await _repository.SaveProfileAsync(ProfileName.Trim(), Options.ToSnapshotJson(), CancellationToken.None);
        await RefreshProfilesAsync();
        StatusMessage = $"Saved profile '{ProfileName.Trim()}'. Subscription keys are never stored in a profile.";
    }

    [RelayCommand]
    private async Task ApplyProfileAsync()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = "Select a profile to apply.";
            return;
        }

        var key = Options.SubscriptionKey;
        var loaded = FaceLabOptions.FromSnapshotJson(SelectedProfile.Json);
        loaded.SubscriptionKey = key;

        Options = loaded;
        SelectedAuthMode = loaded.AuthMode.ToString();
        ProfileName = SelectedProfile.Name;
        await _configStore.SaveAsync(loaded);
        StatusMessage = $"Applied profile '{SelectedProfile.Name}'.";
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = "Select a profile to delete.";
            return;
        }

        var name = SelectedProfile.Name;
        await _repository.DeleteProfileAsync(SelectedProfile.Id, CancellationToken.None);
        SelectedProfile = null;
        await RefreshProfilesAsync();
        StatusMessage = $"Deleted profile '{name}'.";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        StatusMessage = "Calling the Face API...";
        TraceDetail = string.Empty;

        try
        {
            await _configStore.SaveAsync(Options);
            var trace = await _runner.TestConnectionAsync(Options, CancellationToken.None);
            StatusMessage = trace.IsSuccess
                ? $"Success: {trace.Method} {trace.Operation} returned {trace.StatusCode} in {trace.ElapsedMilliseconds} ms."
                : $"Failed: {trace.Method} {trace.Operation} returned {trace.StatusCode?.ToString() ?? "no response"}.";
            TraceDetail = $"{trace.Method} {trace.Url}{Environment.NewLine}{trace.ResponseBody ?? trace.Error}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetDatabaseAsync()
    {
        await _repository.ResetAsync(CancellationToken.None);
        await RefreshProfilesAsync();
        StatusMessage = "Local database cleared: images, runs, traces, people and profiles were deleted.";
    }

    private async Task RefreshProfilesAsync()
    {
        var profiles = await _repository.GetProfilesAsync(CancellationToken.None);
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }
    }
}
