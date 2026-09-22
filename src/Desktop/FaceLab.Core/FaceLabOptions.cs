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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaceLab.Core;

public enum FaceAuthMode
{
    SubscriptionKey,
    DefaultAzureCredential
}

/// <summary>
/// Every Face API knob the desktop test bench can change between runs. Serialized (minus
/// the subscription key) into config profiles and into each run's snapshot so results can
/// be compared across configurations.
/// </summary>
public sealed class FaceLabOptions
{
    public const int FaceApiMaxImageBytes = 6 * 1024 * 1024;

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Endpoint { get; set; } = string.Empty;

    public FaceAuthMode AuthMode { get; set; } = FaceAuthMode.SubscriptionKey;

    /// <summary>Never persisted to the local database - the MAUI app keeps this in SecureStorage.</summary>
    [JsonIgnore]
    public string SubscriptionKey { get; set; } = string.Empty;

    public string ApiVersionSegment { get; set; } = "face/v1.2-preview.1";

    public string DetectionModel { get; set; } = "detection_03";

    public string RecognitionModel { get; set; } = "recognition_04";

    public string DynamicPersonGroupId { get; set; } = "frs-ai-demo-group";

    public double ConfidenceThreshold { get; set; } = 0.6;

    public int MaxCandidatesReturned { get; set; } = 1;

    public int OperationTimeoutSeconds { get; set; } = 30;

    public int OperationPollIntervalMilliseconds { get; set; } = 500;

    /// <summary>When false the run stops after Detect + Identify and never mutates the person directory.</summary>
    public bool AutoEnrollUnmatchedFaces { get; set; } = true;

    public bool EnsureDynamicPersonGroupExists { get; set; } = true;

    public int MaxImageSizeBytes { get; set; } = FaceApiMaxImageBytes;

    /// <summary>Number of face ids sent per Identify request.</summary>
    public int IdentifyBatchSize { get; set; } = 10;

    public TimeSpan OperationTimeout => TimeSpan.FromSeconds(Math.Max(1, OperationTimeoutSeconds));

    public TimeSpan OperationPollInterval => TimeSpan.FromMilliseconds(Math.Max(1, OperationPollIntervalMilliseconds));

    public string BaseAddress => Endpoint.TrimEnd('/') + "/";

    public FaceLabOptions Clone() => (FaceLabOptions)MemberwiseClone();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            errors.Add("Endpoint is required.");
        }
        else if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
        {
            errors.Add("Endpoint must be an absolute URI, for example https://my-face.cognitiveservices.azure.com.");
        }

        if (AuthMode == FaceAuthMode.SubscriptionKey && string.IsNullOrWhiteSpace(SubscriptionKey))
        {
            errors.Add("A subscription key is required when the auth mode is SubscriptionKey.");
        }

        if (string.IsNullOrWhiteSpace(ApiVersionSegment))
        {
            errors.Add("API version segment is required, for example face/v1.2-preview.1.");
        }

        if (string.IsNullOrWhiteSpace(DetectionModel))
        {
            errors.Add("Detection model is required.");
        }

        if (string.IsNullOrWhiteSpace(RecognitionModel))
        {
            errors.Add("Recognition model is required.");
        }

        if (string.IsNullOrWhiteSpace(DynamicPersonGroupId))
        {
            errors.Add("Dynamic person group id is required.");
        }

        if (ConfidenceThreshold is < 0 or > 1)
        {
            errors.Add("Confidence threshold must be between 0 and 1.");
        }

        if (MaxCandidatesReturned < 1)
        {
            errors.Add("Max candidates returned must be at least 1.");
        }

        if (IdentifyBatchSize is < 1 or > 10)
        {
            errors.Add("Identify batch size must be between 1 and 10 (Face API limit).");
        }

        if (MaxImageSizeBytes < 1)
        {
            errors.Add("Max image size must be greater than zero.");
        }

        return errors;
    }

    /// <summary>Serializes the configuration without secrets, for profiles and run snapshots.</summary>
    public string ToSnapshotJson() => JsonSerializer.Serialize(this, SnapshotJsonOptions);

    public static FaceLabOptions FromSnapshotJson(string json) =>
        JsonSerializer.Deserialize<FaceLabOptions>(json, SnapshotJsonOptions) ?? new FaceLabOptions();
}
