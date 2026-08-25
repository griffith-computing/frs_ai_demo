using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using FrsAiDemo.FunctionApp.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration(config =>
    {
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // A single DefaultAzureCredential is shared across Blob Storage, Cosmos DB and the Face
        // API HttpClient. In Azure this resolves to the Function App's user-assigned managed
        // identity (AZURE_CLIENT_ID app setting); locally it falls back to Azure CLI/VS credentials.
        var clientId = configuration["AZURE_CLIENT_ID"];
        var credential = string.IsNullOrWhiteSpace(clientId)
            // Exclude managed identity locally: dev machines with an Azure Arc agent installed cause
            // DefaultAzureCredential to fail hard on an inaccessible Arc token file instead of falling
            // back to Azure CLI/VS credentials.
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId });
        services.AddSingleton<TokenCredential>(credential);

        var storageAccountName = configuration["PhotosStorageAccountName"];
        services.AddSingleton(_ =>
        {
            if (string.IsNullOrWhiteSpace(storageAccountName))
            {
                throw new InvalidOperationException("PhotosStorageAccountName configuration value is required.");
            }
            var blobServiceUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
            return new BlobServiceClient(blobServiceUri, credential);
        });

        var cosmosEndpoint = configuration["CosmosDb:Endpoint"];
        services.AddSingleton(_ =>
        {
            if (string.IsNullOrWhiteSpace(cosmosEndpoint))
            {
                throw new InvalidOperationException("CosmosDb:Endpoint configuration value is required.");
            }
            return new CosmosClient(cosmosEndpoint, credential, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            });
        });

        services.AddHttpClient<IFaceApiService, FaceApiService>();
        services.AddSingleton<IBlobStorageService, BlobStorageService>();
        services.AddSingleton<ICosmosFaceRepository, CosmosFaceRepository>();
        services.AddSingleton<IUploadRepository, CosmosUploadRepository>();
    })
    .Build();

host.Run();
