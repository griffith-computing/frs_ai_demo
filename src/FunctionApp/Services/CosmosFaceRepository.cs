using FrsAiDemo.FunctionApp.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrsAiDemo.FunctionApp.Services;

public interface ICosmosFaceRepository
{
    Task<FaceRecord> CreateFaceRecordAsync(string personId, string personGroupId, RecognitionEvent firstSighting, CancellationToken cancellationToken);
    Task<FaceRecord> RecordRecognitionAsync(string personId, RecognitionEvent sighting, CancellationToken cancellationToken);
}

/// <summary>
/// Cosmos DB (NoSQL API) repository for the Faces container. Partition key is /personId.
/// New faces get a brand-new document; repeat sightings append to recognitionHistory and
/// bump lastSeenUtc on the existing document.
/// </summary>
public sealed class CosmosFaceRepository : ICosmosFaceRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosFaceRepository> _logger;

    public CosmosFaceRepository(CosmosClient cosmosClient, IConfiguration configuration, ILogger<CosmosFaceRepository> logger)
    {
        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "FacialRecognitionDb";
        var containerName = configuration["CosmosDb:FacesContainerName"] ?? "Faces";
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task<FaceRecord> CreateFaceRecordAsync(string personId, string personGroupId, RecognitionEvent firstSighting, CancellationToken cancellationToken)
    {
        var record = new FaceRecord
        {
            Id = personId,
            PersonId = personId,
            PersonGroupId = personGroupId,
            FirstSeenUtc = firstSighting.TimestampUtc,
            LastSeenUtc = firstSighting.TimestampUtc,
            RecognitionHistory = { firstSighting }
        };

        var response = await _container.CreateItemAsync(record, new PartitionKey(personId), cancellationToken: cancellationToken);
        _logger.LogInformation("Created new Cosmos Faces record for personId {PersonId}", personId);
        return response.Resource;
    }

    public async Task<FaceRecord> RecordRecognitionAsync(string personId, RecognitionEvent sighting, CancellationToken cancellationToken)
    {
        var existing = await _container.ReadItemAsync<FaceRecord>(personId, new PartitionKey(personId), cancellationToken: cancellationToken);
        var record = existing.Resource;

        record.RecognitionHistory.Add(sighting);
        record.LastSeenUtc = sighting.TimestampUtc;

        var response = await _container.ReplaceItemAsync(record, personId, new PartitionKey(personId), cancellationToken: cancellationToken);
        _logger.LogInformation("Updated Cosmos Faces record for personId {PersonId}, lastSeenUtc {LastSeenUtc}", personId, sighting.TimestampUtc);
        return response.Resource;
    }
}
