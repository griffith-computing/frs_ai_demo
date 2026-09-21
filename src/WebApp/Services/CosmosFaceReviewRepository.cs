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

using FrsAiDemo.WebApp.Models;
using Microsoft.Azure.Cosmos;

namespace FrsAiDemo.WebApp.Services;

public interface IFaceReviewRepository
{
    Task<PageResult<FaceRecord>> GetPeopleAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);
    Task<FaceRecord?> GetPersonAsync(string personId, CancellationToken cancellationToken);
    Task<UploadRecord?> GetUploadAsync(string uploadId, CancellationToken cancellationToken);
    Task<UploadBatchRecord?> GetBatchAsync(string batchId, CancellationToken cancellationToken);
    Task<StorageImportRecord?> GetImportAsync(string importId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UploadRecord>> GetBatchUploadsAsync(string batchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UploadBatchRecord>> GetImportBatchesAsync(string importId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UploadRecord>> GetImportUploadsAsync(string importId, CancellationToken cancellationToken);
    Task<UploadStatusCounts> GetImportStatusCountsAsync(string importId, CancellationToken cancellationToken);
    Task CreateUploadAsync(UploadRecord upload, CancellationToken cancellationToken);
    Task CreateBatchAsync(UploadBatchRecord batch, CancellationToken cancellationToken);
    Task<bool> TryReserveBatchSlotAsync(string batchId, CancellationToken cancellationToken);
    Task CompleteBatchSubmissionAsync(string batchId, CancellationToken cancellationToken);
    Task CreateImportAsync(StorageImportRecord import, CancellationToken cancellationToken);
    Task SetUploadStatusAsync(string uploadId, string status, string? failureSummary, CancellationToken cancellationToken);
    Task<ReviewRecord?> GetReviewAsync(string personId, string sightingKey, string reviewerObjectId, CancellationToken cancellationToken);
    Task<ReviewRecord> UpsertReviewAsync(ReviewInput input, string reviewerObjectId, string reviewerName, CancellationToken cancellationToken);
}

public sealed class CosmosFaceReviewRepository : IFaceReviewRepository
{
    private readonly Container _faces;
    private readonly Container _uploads;
    private readonly Container _reviews;

    public CosmosFaceReviewRepository(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "FacialRecognitionDb";
        _faces = cosmosClient.GetContainer(databaseName, configuration["CosmosDb:FacesContainerName"] ?? "Faces");
        _uploads = cosmosClient.GetContainer(databaseName, configuration["CosmosDb:UploadsContainerName"] ?? "Uploads");
        _reviews = cosmosClient.GetContainer(databaseName, configuration["CosmosDb:ReviewsContainerName"] ?? "Reviews");
    }

    public async Task<PageResult<FaceRecord>> GetPeopleAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        var iterator = _faces.GetItemQueryIterator<FaceRecord>(
            new QueryDefinition("SELECT * FROM c ORDER BY c.lastSeenUtc DESC"),
            continuationToken,
            new QueryRequestOptions { MaxItemCount = Math.Clamp(pageSize, 1, 50) });
        var page = await iterator.ReadNextAsync(cancellationToken);
        return new PageResult<FaceRecord>(page.ToList(), page.ContinuationToken);
    }

    public async Task<FaceRecord?> GetPersonAsync(string personId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _faces.ReadItemAsync<FaceRecord>(personId, new PartitionKey(personId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<UploadRecord?> GetUploadAsync(string uploadId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _uploads.ReadItemAsync<UploadRecord>(uploadId, new PartitionKey(uploadId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<UploadBatchRecord?> GetBatchAsync(string batchId, CancellationToken cancellationToken) =>
        ReadAsync<UploadBatchRecord>(batchId, cancellationToken);

    public Task<StorageImportRecord?> GetImportAsync(string importId, CancellationToken cancellationToken) =>
        ReadAsync<StorageImportRecord>(importId, cancellationToken);

    public Task<IReadOnlyList<UploadRecord>> GetBatchUploadsAsync(string batchId, CancellationToken cancellationToken) =>
        QueryAsync<UploadRecord>(
            new QueryDefinition("SELECT * FROM c WHERE c.documentType = 'upload' AND c.batchId = @batchId")
                .WithParameter("@batchId", batchId),
            cancellationToken);

    public Task<IReadOnlyList<UploadBatchRecord>> GetImportBatchesAsync(string importId, CancellationToken cancellationToken) =>
        QueryAsync<UploadBatchRecord>(
            new QueryDefinition("SELECT * FROM c WHERE c.documentType = 'batch' AND c.importId = @importId ORDER BY c.createdUtc")
                .WithParameter("@importId", importId),
            cancellationToken);

    public Task<IReadOnlyList<UploadRecord>> GetImportUploadsAsync(string importId, CancellationToken cancellationToken) =>
        QueryAsync<UploadRecord>(
            new QueryDefinition("SELECT * FROM c WHERE c.documentType = 'upload' AND c.importId = @importId")
                .WithParameter("@importId", importId),
            cancellationToken);

    public async Task<UploadStatusCounts> GetImportStatusCountsAsync(string importId, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<StatusCountResult>(
            new QueryDefinition(
                    "SELECT c.status, COUNT(1) AS count FROM c WHERE c.documentType = 'upload' AND c.importId = @importId GROUP BY c.status")
                .WithParameter("@importId", importId),
            cancellationToken);
        var counts = rows.ToDictionary(x => x.Status, x => x.Count, StringComparer.Ordinal);
        int Get(string status) => counts.GetValueOrDefault(status);
        return new UploadStatusCounts(
            rows.Sum(x => x.Count),
            Get("Queued"),
            Get("Processing"),
            Get("Completed"),
            Get("Failed"),
            Get("NoFaces"));
    }

    public async Task CreateUploadAsync(UploadRecord upload, CancellationToken cancellationToken)
    {
        await _uploads.CreateItemAsync(upload, new PartitionKey(upload.Id), cancellationToken: cancellationToken);
    }

    public async Task CreateBatchAsync(UploadBatchRecord batch, CancellationToken cancellationToken)
    {
        await _uploads.CreateItemAsync(batch, new PartitionKey(batch.Id), cancellationToken: cancellationToken);
    }

    public async Task<bool> TryReserveBatchSlotAsync(string batchId, CancellationToken cancellationToken)
    {
        try
        {
            await _uploads.PatchItemAsync<UploadBatchRecord>(
                batchId,
                new PartitionKey(batchId),
                [
                    PatchOperation.Increment("/submittedFileCount", 1),
                    PatchOperation.Set("/updatedUtc", DateTimeOffset.UtcNow)
                ],
                new PatchItemRequestOptions
                {
                    FilterPredicate = "FROM c WHERE c.submissionCompleted = false AND c.submittedFileCount < c.expectedFileCount"
                },
                cancellationToken);
            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
    }

    public async Task CompleteBatchSubmissionAsync(string batchId, CancellationToken cancellationToken)
    {
        await _uploads.PatchItemAsync<UploadBatchRecord>(
            batchId,
            new PartitionKey(batchId),
            [
                PatchOperation.Set("/submissionCompleted", true),
                PatchOperation.Set("/updatedUtc", DateTimeOffset.UtcNow)
            ],
            cancellationToken: cancellationToken);
    }

    public async Task CreateImportAsync(StorageImportRecord import, CancellationToken cancellationToken)
    {
        await _uploads.CreateItemAsync(import, new PartitionKey(import.Id), cancellationToken: cancellationToken);
    }

    public async Task SetUploadStatusAsync(string uploadId, string status, string? failureSummary, CancellationToken cancellationToken)
    {
        var operations = new List<PatchOperation>
        {
            PatchOperation.Set("/status", status),
            PatchOperation.Set("/updatedUtc", DateTimeOffset.UtcNow)
        };
        if (failureSummary is not null)
        {
            operations.Add(PatchOperation.Set("/failureSummary", failureSummary));
        }

        await _uploads.PatchItemAsync<UploadRecord>(
            uploadId,
            new PartitionKey(uploadId),
            operations,
            cancellationToken: cancellationToken);
    }

    private async Task<T?> ReadAsync<T>(string id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _uploads.ReadItemAsync<T>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(QueryDefinition query, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        using var iterator = _uploads.GetItemQueryIterator<T>(query, requestOptions: new QueryRequestOptions { MaxItemCount = 100 });
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }
        return results;
    }

    public async Task<ReviewRecord?> GetReviewAsync(
        string personId,
        string sightingKey,
        string reviewerObjectId,
        CancellationToken cancellationToken)
    {
        var reviewId = SightingKeys.CreateReviewId(sightingKey, reviewerObjectId);
        try
        {
            var response = await _reviews.ReadItemAsync<ReviewRecord>(reviewId, new PartitionKey(personId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ReviewRecord> UpsertReviewAsync(
        ReviewInput input,
        string reviewerObjectId,
        string reviewerName,
        CancellationToken cancellationToken)
    {
        var existing = await GetReviewAsync(input.PersonId, input.SightingKey, reviewerObjectId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var review = new ReviewRecord
        {
            Id = SightingKeys.CreateReviewId(input.SightingKey, reviewerObjectId),
            PersonId = input.PersonId,
            SightingKey = input.SightingKey,
            ReviewerObjectId = reviewerObjectId,
            ReviewerName = reviewerName,
            Decision = input.Decision.ToString(),
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            CreatedUtc = existing?.CreatedUtc ?? now,
            UpdatedUtc = now
        };
        var response = await _reviews.UpsertItemAsync(review, new PartitionKey(input.PersonId), cancellationToken: cancellationToken);
        return response.Resource;
    }
}