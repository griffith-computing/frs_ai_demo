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

using FrsAiDemo.FunctionApp.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrsAiDemo.FunctionApp.Services;

public interface IUploadRepository
{
    Task<UploadRecord> CreateAsync(UploadRecord upload, CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(UploadRecord upload, CancellationToken cancellationToken);
    Task<UploadRecord?> GetAsync(string uploadId, CancellationToken cancellationToken);
    Task<bool> TrySetProcessingAsync(string uploadId, string leaseOwner, CancellationToken cancellationToken);
    Task RenewProcessingLeaseAsync(string uploadId, string leaseOwner, CancellationToken cancellationToken);
    Task<StorageImportRecord?> GetNextImportAsync(CancellationToken cancellationToken);
    Task CreateBatchAsync(UploadBatchRecord batch, CancellationToken cancellationToken);
    Task UpdateImportAsync(StorageImportRecord import, CancellationToken cancellationToken);
    Task SetStatusAsync(
        string uploadId,
        string status,
        int? detectedFaceCount,
        string? failureSummary,
        CancellationToken cancellationToken,
        string? leaseOwner = null);
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

    public async Task<bool> TryCreateAsync(UploadRecord upload, CancellationToken cancellationToken)
    {
        try
        {
            await CreateAsync(upload, cancellationToken);
            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<UploadRecord?> GetAsync(string uploadId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<UploadRecord>(
                uploadId,
                new PartitionKey(uploadId),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> TrySetProcessingAsync(
        string uploadId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.AddMinutes(10);
        var cutoff = now.ToString("O");
        try
        {
            await _container.PatchItemAsync<UploadRecord>(
                uploadId,
                new PartitionKey(uploadId),
                [
                    PatchOperation.Set("/status", UploadStatuses.Processing),
                    PatchOperation.Set("/updatedUtc", now),
                    PatchOperation.Set("/processingLeaseUntilUtc", leaseUntil),
                    PatchOperation.Set("/processingLeaseOwner", leaseOwner)
                ],
                new PatchItemRequestOptions
                {
                    FilterPredicate =
                        $"FROM c WHERE c.status = 'Queued' OR (c.status = 'Processing' AND (NOT IS_DEFINED(c.processingLeaseUntilUtc) OR c.processingLeaseUntilUtc < '{cutoff}'))"
                },
                cancellationToken);
            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Upload record {UploadId} does not exist; processing a pre-status-schema event",
                uploadId);
            return true;
        }
    }

    public async Task RenewProcessingLeaseAsync(
        string uploadId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        await _container.PatchItemAsync<UploadRecord>(
            uploadId,
            new PartitionKey(uploadId),
            [PatchOperation.Set("/processingLeaseUntilUtc", DateTimeOffset.UtcNow.AddMinutes(10))],
            new PatchItemRequestOptions
            {
                FilterPredicate = $"FROM c WHERE c.status = 'Processing' AND c.processingLeaseOwner = '{leaseOwner}'"
            },
            cancellationToken);
    }

    public async Task<StorageImportRecord?> GetNextImportAsync(CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
            "SELECT TOP 1 * FROM c WHERE c.documentType = 'import' AND (c.status = 'Pending' OR c.status = 'Running') ORDER BY c.createdUtc");
        using var iterator = _container.GetItemQueryIterator<StorageImportRecord>(
            query,
            requestOptions: new QueryRequestOptions { MaxItemCount = 1 });
        if (!iterator.HasMoreResults)
        {
            return null;
        }
        var page = await iterator.ReadNextAsync(cancellationToken);
        return page.FirstOrDefault();
    }

    public async Task CreateBatchAsync(UploadBatchRecord batch, CancellationToken cancellationToken)
    {
        await _container.UpsertItemAsync(batch, new PartitionKey(batch.Id), cancellationToken: cancellationToken);
    }

    public async Task UpdateImportAsync(StorageImportRecord import, CancellationToken cancellationToken)
    {
        import.UpdatedUtc = DateTimeOffset.UtcNow;
        await _container.UpsertItemAsync(import, new PartitionKey(import.Id), cancellationToken: cancellationToken);
    }

    public async Task SetStatusAsync(
        string uploadId,
        string status,
        int? detectedFaceCount,
        string? failureSummary,
        CancellationToken cancellationToken,
        string? leaseOwner = null)
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
            await _container.PatchItemAsync<UploadRecord>(
                uploadId,
                new PartitionKey(uploadId),
                operations,
                leaseOwner is null
                    ? null
                    : new PatchItemRequestOptions
                    {
                        FilterPredicate = $"FROM c WHERE c.processingLeaseOwner = '{leaseOwner}'"
                    },
                cancellationToken);
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