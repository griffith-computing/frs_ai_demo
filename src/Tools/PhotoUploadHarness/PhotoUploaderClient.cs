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

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

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
    private readonly TokenCredential? _credential;

    public PhotoUploaderClient(HttpClient httpClient, HarnessOptions options, TokenCredential? credential = null)
    {
        if (options.RequiresEntraAuthentication && string.IsNullOrWhiteSpace(options.EntraClientId))
        {
            throw new ArgumentException(
                "EntraClientId is required for a non-local upload endpoint.",
                nameof(options));
        }

        _httpClient = httpClient;
        _options = options;
        _credential = credential ?? (options.RequiresEntraAuthentication
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = true,
                ExcludeWorkloadIdentityCredential = true,
                ExcludeManagedIdentityCredential = true
            })
            : null);
    }

    public async Task<UploadResult> UploadAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var contentType = file.Extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };

        try
        {
            await using var stream = file.OpenRead();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = content
            };

            if (_options.RequiresEntraAuthentication)
            {
                var token = await _credential!.GetTokenAsync(
                    new TokenRequestContext([$"api://{_options.EntraClientId}/.default"]),
                    cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
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
