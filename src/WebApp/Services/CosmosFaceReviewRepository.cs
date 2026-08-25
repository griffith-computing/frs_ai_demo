using FrsAiDemo.WebApp.Models;
using Microsoft.Azure.Cosmos;

namespace FrsAiDemo.WebApp.Services;

public interface IFaceReviewRepository
{
    Task<PageResult<FaceRecord>> GetPeopleAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);
    Task<FaceRecord?> GetPersonAsync(string personId, CancellationToken cancellationToken);
    Task<UploadRecord?> GetUploadAsync(string uploadId, CancellationToken cancellationToken);
    Task CreateUploadAsync(UploadRecord upload, CancellationToken cancellationToken);
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

    public async Task CreateUploadAsync(UploadRecord upload, CancellationToken cancellationToken)
    {
        await _uploads.CreateItemAsync(upload, new PartitionKey(upload.Id), cancellationToken: cancellationToken);
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