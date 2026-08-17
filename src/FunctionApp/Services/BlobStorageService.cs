using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace FrsAiDemo.FunctionApp.Services;

public interface IBlobStorageService
{
    Task<string> UploadPhotoAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream> DownloadPhotoAsync(string containerName, string blobName, CancellationToken cancellationToken);
    Task UploadPoisonMessageAsync(string blobName, string content, CancellationToken cancellationToken);
}

/// <summary>
/// Wraps Blob Storage access for photo upload/download. Uses the BlobServiceClient
/// registered in DI, which is configured with DefaultAzureCredential (managed identity).
/// </summary>
public sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;

    public BlobStorageService(BlobServiceClient blobServiceClient, IConfiguration configuration)
    {
        _blobServiceClient = blobServiceClient;
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

    public async Task<Stream> DownloadPhotoAsync(string containerName, string blobName, CancellationToken cancellationToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
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
