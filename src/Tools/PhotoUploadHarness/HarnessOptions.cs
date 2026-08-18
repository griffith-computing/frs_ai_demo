namespace FrsAiDemo.PhotoUploadHarness;

/// <summary>Configuration options for the harness, bound from appsettings.json, env vars and CLI args.</summary>
public sealed class HarnessOptions
{
    public string BaseUrl { get; set; } = "http://localhost:7071/api/photos";
    public string FunctionKey { get; set; } = string.Empty;
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
}

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "FacialRecognitionDb";
    public string FacesContainerName { get; set; } = "Faces";
}
