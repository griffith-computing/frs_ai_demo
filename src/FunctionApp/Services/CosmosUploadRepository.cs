using FrsAiDemo.FunctionApp.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrsAiDemo.FunctionApp.Services;

public interface IUploadRepository
{
    Task<UploadRecord> CreateAsync(UploadRecord upload, CancellationToken cancellationToken);
    Task SetStatusAsync(string uploadId, string status, int? detectedFaceCount, string? failureSummary, CancellationToken cancellationToken);
}

public sealed class CosmosUploadRepository : IUploadRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosUploadRepository> _logger;

    public CosmosUploadRepository(CosmosClient cosmosClient, IConfiguration configuration, ILogger<CosmosUploadRepository> logger)
    {
        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "FacialRecognitionDb";
        var containerName = configuration["CosmosDb:UploadsContainerName"] ?? "Uploads";
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task<UploadRecord> CreateAsync(UploadRecord upload, CancellationToken cancellationToken)
    {
        var response = await _container.CreateItemAsync(upload, new PartitionKey(upload.Id), cancellationToken: cancellationToken);
        _logger.LogInformation("Created upload record {UploadId} with status {Status}", upload.Id, upload.Status);
        return response.Resource;
    }

    public async Task SetStatusAsync(
        string uploadId,
        string status,
        int? detectedFaceCount,
        string? failureSummary,
        CancellationToken cancellationToken)
    {
        var operations = new List<PatchOperation>
        {
            PatchOperation.Set("/status", status),
            PatchOperation.Set("/updatedUtc", DateTimeOffset.UtcNow)
        };

        if (detectedFaceCount.HasValue)
        {
            operations.Add(PatchOperation.Set("/detectedFaceCount", detectedFaceCount.Value));
        }

        if (failureSummary is not null)
        {
            operations.Add(PatchOperation.Set("/failureSummary", failureSummary));
        }

        try
        {
            await _container.PatchItemAsync<UploadRecord>(uploadId, new PartitionKey(uploadId), operations, cancellationToken: cancellationToken);
            _logger.LogInformation("Updated upload {UploadId} to status {Status}", uploadId, status);
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Upload record {UploadId} does not exist; continuing processing for a pre-status-schema event",
                uploadId);
        }
    }
}