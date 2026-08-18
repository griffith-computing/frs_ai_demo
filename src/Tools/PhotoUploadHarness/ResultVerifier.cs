using Azure.Identity;
using Microsoft.Azure.Cosmos;

namespace FrsAiDemo.PhotoUploadHarness;

/// <summary>Matches a subset of FaceRecord/RecognitionEvent from the FunctionApp's Cosmos schema.</summary>
public sealed class FaceRecordLite
{
    public string Id { get; set; } = string.Empty;
    public string PersonId { get; set; } = string.Empty;
    public List<RecognitionEventLite> RecognitionHistory { get; set; } = new();
}

public sealed class RecognitionEventLite
{
    public DateTimeOffset TimestampUtc { get; set; }
    public string BlobUrl { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string? FaceId { get; set; }
}

public sealed class VerificationResult
{
    public bool Recognized { get; init; }
    public string? PersonId { get; init; }
    public double? Confidence { get; init; }
}

/// <summary>Polls Cosmos DB for the FaceRecord produced once ProcessPhotoFunction finishes with a given blob.</summary>
public sealed class ResultVerifier
{
    private readonly Container _container;
    private readonly HarnessOptions _options;

    public ResultVerifier(HarnessOptions options)
    {
        _options = options;
        var credential = new DefaultAzureCredential();
        var cosmosClient = new CosmosClient(options.Cosmos.Endpoint, credential, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });
        _container = cosmosClient.GetContainer(options.Cosmos.DatabaseName, options.Cosmos.FacesContainerName);
    }

    public async Task<VerificationResult> WaitForRecognitionAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.VerificationTimeoutSeconds);
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE ARRAY_CONTAINS(c.recognitionHistory, {\"blobUrl\": @blobUrl}, true)")
            .WithParameter("@blobUrl", blobUrl);

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            using var iterator = _container.GetItemQueryIterator<FaceRecordLite>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                var match = page.FirstOrDefault();
                if (match is not null)
                {
                    var recognitionEvent = match.RecognitionHistory.FirstOrDefault(e => e.BlobUrl == blobUrl);
                    return new VerificationResult
                    {
                        Recognized = true,
                        PersonId = match.PersonId,
                        Confidence = recognitionEvent?.Confidence
                    };
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.VerificationPollIntervalSeconds), cancellationToken);
        }

        return new VerificationResult { Recognized = false };
    }
}
