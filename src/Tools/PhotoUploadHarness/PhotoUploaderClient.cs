using System.Net.Http.Headers;
using System.Text.Json;

namespace FrsAiDemo.PhotoUploadHarness;

/// <summary>Mirrors the JSON body returned by UploadPhotoFunction's 202 Accepted response.</summary>
public sealed class UploadResponseDto
{
    public string UploadId { get; set; } = string.Empty;
    public string BlobUrl { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
}

public sealed class UploadResult
{
    public required string FileName { get; init; }
    public bool Success { get; init; }
    public UploadResponseDto? Response { get; init; }
    public int StatusCode { get; init; }
    public string? Error { get; init; }
}

/// <summary>Posts raw photo bytes to the UploadPhotoFunction HTTP endpoint.</summary>
public sealed class PhotoUploaderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly HarnessOptions _options;

    public PhotoUploaderClient(HttpClient httpClient, HarnessOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<UploadResult> UploadAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var contentType = file.Extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };

        var requestUri = _options.BaseUrl;
        if (!string.IsNullOrWhiteSpace(_options.FunctionKey))
        {
            var separator = requestUri.Contains('?') ? '&' : '?';
            requestUri = $"{requestUri}{separator}code={Uri.EscapeDataString(_options.FunctionKey)}";
        }

        try
        {
            await using var stream = file.OpenRead();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var response = await _httpClient.PostAsync(requestUri, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new UploadResult
                {
                    FileName = file.Name,
                    Success = false,
                    StatusCode = (int)response.StatusCode,
                    Error = body
                };
            }

            var parsed = JsonSerializer.Deserialize<UploadResponseDto>(body, JsonOptions);
            return new UploadResult
            {
                FileName = file.Name,
                Success = true,
                StatusCode = (int)response.StatusCode,
                Response = parsed
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UploadResult { FileName = file.Name, Success = false, Error = ex.Message };
        }
    }
}
