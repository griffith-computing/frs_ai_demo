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

using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace FrsAiDemo.FunctionApp.Services;

public interface IBlobStorageService
{
    Task<string> UploadPhotoAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream> DownloadPhotoAsync(string containerName, string blobName, CancellationToken cancellationToken, string? storageAccountName = null);
    Task UploadPoisonMessageAsync(string blobName, string content, CancellationToken cancellationToken);
}

/// <summary>
/// Wraps Blob Storage access for photo upload/download. Uses the BlobServiceClient
/// registered in DI, which is configured with DefaultAzureCredential (managed identity).
/// </summary>
public sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly Azure.Core.TokenCredential _credential;
    private readonly string _accountName;
    private readonly IConfiguration _configuration;
    private readonly string _containerName;

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        Azure.Core.TokenCredential credential,
        IConfiguration configuration)
    {
        _blobServiceClient = blobServiceClient;
        _credential = credential;
        _configuration = configuration;
        _accountName = configuration["PhotosStorageAccountName"]
            ?? throw new InvalidOperationException("PhotosStorageAccountName is required.");
        _containerName = configuration["PhotosContainerName"] ?? "photos";
    }

    public async Task<string> UploadPhotoAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(content, overwrite: true, cancellationToken: cancellationToken);
        return blobClient.Uri.ToString();
    }

    public async Task<Stream> DownloadPhotoAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken,
        string? storageAccountName = null)
    {
        var client = string.IsNullOrWhiteSpace(storageAccountName)
            || string.Equals(storageAccountName, _accountName, StringComparison.OrdinalIgnoreCase)
            ? _blobServiceClient
            : CreateAllowlistedSourceClient(storageAccountName, containerName);
        var containerClient = client.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    private BlobServiceClient CreateAllowlistedSourceClient(string storageAccountName, string containerName)
    {
        if (!_configuration.GetValue<bool>("BulkUploads:Enabled"))
        {
            throw new InvalidOperationException("In-place storage imports are disabled.");
        }
        var sources = JsonSerializer.Deserialize<List<ImportSource>>(
            _configuration["BulkUploads:SourcesJson"] ?? "[]",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        if (!sources.Any(x =>
            x.AllowInPlace
            && string.Equals(x.AccountName, storageAccountName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ContainerName, containerName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The source storage location is not allowlisted for in-place processing.");
        }
        return new BlobServiceClient(
            new Uri($"https://{storageAccountName}.blob.core.windows.net"),
            _credential);
    }

    private sealed class ImportSource
    {
        public required string AccountName { get; init; }
        public required string ContainerName { get; init; }
        public bool AllowInPlace { get; init; }
    }

    public async Task UploadPoisonMessageAsync(string blobName, string content, CancellationToken cancellationToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient("poison-messages");
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
    }
}
