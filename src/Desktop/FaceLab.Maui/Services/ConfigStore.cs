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

using FaceLab.Core;

namespace FaceLab.Maui.Services;

public interface IConfigStore
{
    /// <summary>The configuration every page runs against. Mutated in place by the config screen.</summary>
    FaceLabOptions Current { get; }

    Task<FaceLabOptions> LoadAsync();

    Task SaveAsync(FaceLabOptions options);

    void Replace(FaceLabOptions options);
}

/// <summary>
/// Persists the active configuration between app launches: everything except the subscription
/// key goes to <see cref="Preferences"/>; the key goes to <see cref="SecureStorage"/> and is
/// never written to the local database or a config profile.
/// </summary>
public sealed class ConfigStore : IConfigStore
{
    private const string PreferencesKey = "facelab.options";
    private const string SecureStorageKey = "facelab.subscriptionKey";

    private FaceLabOptions _current = new();
    private bool _loaded;

    public FaceLabOptions Current => _current;

    public async Task<FaceLabOptions> LoadAsync()
    {
        if (_loaded)
        {
            return _current;
        }

        var json = Preferences.Default.Get<string?>(PreferencesKey, null);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                _current = FaceLabOptions.FromSnapshotJson(json);
            }
            catch (Exception)
            {
                _current = new FaceLabOptions();
            }
        }

        try
        {
            _current.SubscriptionKey = await SecureStorage.Default.GetAsync(SecureStorageKey) ?? string.Empty;
        }
        catch (Exception)
        {
            // SecureStorage is unavailable in some unpackaged/test contexts; fall back to an empty key.
            _current.SubscriptionKey = string.Empty;
        }

        _loaded = true;
        return _current;
    }

    public async Task SaveAsync(FaceLabOptions options)
    {
        _current = options;
        _loaded = true;

        Preferences.Default.Set(PreferencesKey, options.ToSnapshotJson());

        try
        {
            if (string.IsNullOrWhiteSpace(options.SubscriptionKey))
            {
                SecureStorage.Default.Remove(SecureStorageKey);
            }
            else
            {
                await SecureStorage.Default.SetAsync(SecureStorageKey, options.SubscriptionKey);
            }
        }
        catch (Exception)
        {
            // Saving the key is best-effort; the session in memory still works.
        }
    }

    public void Replace(FaceLabOptions options)
    {
        _current = options;
        _loaded = true;
    }
}
