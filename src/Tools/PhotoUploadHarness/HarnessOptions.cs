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

namespace FrsAiDemo.PhotoUploadHarness;

/// <summary>Configuration options for the harness, bound from appsettings.json, env vars and CLI args.</summary>
public sealed class HarnessOptions
{
    public string BaseUrl { get; set; } = "http://localhost:7071/api/photos";
    public string EntraClientId { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>"batch" (upload every file once) or "continuous" (loop with a delay, simulating a camera feed).</summary>
    public string Mode { get; set; } = "batch";
    public int IntervalSeconds { get; set; } = 5;

    /// <summary>Only used in continuous mode. 0 means loop until cancelled (Ctrl+C).</summary>
    public int MaxIterations { get; set; }

    public bool EnableVerification { get; set; }
    public int VerificationTimeoutSeconds { get; set; } = 60;
    public int VerificationPollIntervalSeconds { get; set; } = 3;

    public CosmosOptions Cosmos { get; set; } = new();

    public bool IsContinuous => string.Equals(Mode, "continuous", StringComparison.OrdinalIgnoreCase);

    public bool RequiresEntraAuthentication =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && !uri.IsLoopback;
}

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "FacialRecognitionDb";
    public string FacesContainerName { get; set; } = "Faces";
}
