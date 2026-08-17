# frs_ai_demo — Facial Recognition Ingestion Pipeline

An Azure solution that lets users upload photos, automatically detects and
recognizes faces in each photo using **Azure AI Face API**, and logs the
results to **Cosmos DB**: a brand-new face creates a new record, while a
recognized (previously seen) face gets its last-seen timestamp and history
updated.

> **Note on "Open AI":** Azure OpenAI does not perform facial recognition —
> that capability is provided by **Azure AI Vision's Face API**. This
> solution uses the Face API for face detection/identification. If you
> specifically need an Azure OpenAI (GPT-4o vision) component (e.g. for
> generating a natural-language description of a photo), that can be added
> as an additional step alongside the Face API calls.

## Architecture

```
                 ┌────────────────────┐
  Client / App   │ UploadPhotoFunction │  (HTTP trigger)
  ─────POST─────▶│  - stores photo in  │──────┐
                 │    Blob Storage     │      │
                 │  - publishes event  │      │ blob URL + metadata
                 └────────────────────┘      ▼
                                        ┌───────────┐
                                        │ Event Hub │  (photo-events)
                                        └───────────┘
                                              │
                                              ▼
                 ┌──────────────────────┐
                 │ ProcessPhotoFunction │  (Event Hub trigger)
                 │  1. fetch blob       │
                 │  2. Face API Detect  │───▶ Azure AI Face API
                 │  3. Face API Identify│      (PersonGroup: frs-ai-demo-group)
                 │  4. new? create      │
                 │     Person + train   │
                 │  5. write/update     │───▶ Cosmos DB (Faces container)
                 │     Cosmos record    │
                 └──────────────────────┘
```

**Why Blob Storage + Event Hub (not photo bytes directly in Event Hub)?**
Event Hub messages are capped at 1 MB (Standard tier) or up to 20 MB
(Premium/Dedicated). Real photos routinely exceed this, and Event Hub isn't
designed for large binary payloads. Instead, the photo is uploaded to Blob
Storage first, and Event Hub only carries a small JSON event with the blob
URL — a standard, scalable pattern for this kind of pipeline.

**Why Face API PersonGroup/Identify instead of custom vector similarity?**
Azure AI Face API does not expose raw face-embedding vectors through its
public API (a responsible-AI restriction), so a genuinely "custom" vector
similarity search against Cosmos DB isn't possible with this service. The
Face API's own **PersonGroup** + **Identify** operations do this matching
for you server-side — you manage a group of known people, and Identify
returns candidate `personId` matches with confidence scores. This solution
still uses Cosmos DB to store rich metadata per person (first/last seen,
full recognition history).

## Components

| Component | Purpose |
|---|---|
| **Blob Storage** (`photos` container) | Stores uploaded photo files. |
| **Event Hub** (`photo-events`) | Carries lightweight upload events (blob URL + metadata) from the upload function to the processing function. |
| **Function App** (.NET 8 isolated) | `UploadPhotoFunction` (HTTP trigger) + `ProcessPhotoFunction` (Event Hub trigger). |
| **Azure AI Face API** | Face detection (`Detect`) and recognition (`Identify` against a `PersonGroup`); new faces are registered as new `Person` entries and the group is retrained. |
| **Cosmos DB** (NoSQL API, `Faces` container, partition key `/personId`) | One document per recognized person: `firstSeenUtc`, `lastSeenUtc`, and a `recognitionHistory` array of every sighting (timestamp, blob URL, confidence). |
| **Managed Identity** | Single user-assigned identity used by the Function App to authenticate to Storage, Event Hub, Cosmos DB, and the Face API — no connection strings or keys stored in app settings. |

## Repository layout

```
/infra/
  main.bicep                 # orchestrates all modules
  modules/
    identity.bicep           # user-assigned managed identity
    storage.bicep            # storage account + "photos" blob container
    eventhub.bicep            # Event Hub namespace + hub + consumer group
    cosmos.bicep              # Cosmos DB account + database + Faces container
    face.bicep                # Azure AI Face API (Cognitive Services) account
    appinsights.bicep         # Log Analytics + Application Insights
    functionapp.bicep         # Function App + App Service plan + app settings
    rbac.bicep                # role assignments granting the identity access
/src/FunctionApp/
  Program.cs                  # DI setup (DefaultAzureCredential, clients)
  UploadPhotoFunction.cs       # HTTP trigger: upload photo -> blob + event
  ProcessPhotoFunction.cs      # Event Hub trigger: detect/identify/log
  Services/
    BlobStorageService.cs
    FaceApiService.cs          # Face API REST wrapper (Detect/Identify/PersonGroup/Train)
    CosmosFaceRepository.cs
  Models/
    PhotoUploadedEvent.cs
    FaceRecord.cs               # Cosmos document + RecognitionEvent
    FaceApiModels.cs            # Face API REST request/response DTOs
  host.json
  local.settings.json.example   # copy to local.settings.json for local dev
```

## Deploying the infrastructure

Prerequisites: [Azure CLI](https://learn.microsoft.com/cli/azure/) with the
Bicep extension (`az bicep install`), and an Azure subscription with access
to create Cognitive Services (Face) resources (this may require approval —
see [Face API limited access](https://learn.microsoft.com/legal/cognitive-services/computer-vision/limited-access-identity)).

```powershell
az login
az group create --name rg-frs-ai-demo --location eastus

az deployment group create `
  --resource-group rg-frs-ai-demo `
  --template-file infra/main.bicep `
  --parameters namePrefix=frsaidemo location=eastus
```

Validate templates without deploying:

```powershell
az bicep build --file infra/main.bicep --stdout
az deployment group what-if --resource-group rg-frs-ai-demo --template-file infra/main.bicep
```

After deployment, note the outputs (`functionAppName`, `storageAccountName`,
etc.) — you'll need the Function App's publish settings/function key to call
the upload endpoint, and the Face API resource must have **Limited Access**
approval from Microsoft before the `Identify`/`PersonGroup` operations will
work in a production subscription.

## Deploying the Function App code

```powershell
cd src/FunctionApp
dotnet publish -c Release -o publish
# then zip-deploy, e.g.:
Compress-Archive -Path publish\* -DestinationPath publish.zip -Force
az functionapp deployment source config-zip --resource-group rg-frs-ai-demo --name <functionAppName> --src publish.zip
```

## Local development

1. Copy `src/FunctionApp/local.settings.json.example` to
   `src/FunctionApp/local.settings.json` and fill in the resource names/
   endpoints from your deployment (or an existing dev environment).
2. Ensure you're signed in with `az login` (or have Visual Studio/VS Code
   Azure credentials cached) — `DefaultAzureCredential` will fall back to
   these for local runs instead of the Function App's managed identity.
3. Install [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
   (`func`) and run:
   ```powershell
   cd src/FunctionApp
   func start
   ```
4. Upload a test photo:
   ```powershell
   curl -X POST "http://localhost:7071/api/photos" `
     -H "Content-Type: image/jpeg" `
     --data-binary "@C:\path\to\photo.jpg"
   ```

## Building / validating

```powershell
# .NET Function App
cd src/FunctionApp
dotnet build

# Bicep templates
az bicep build --file infra/main.bicep --stdout
```

## Known limitations / follow-ups

- **PersonGroup training is asynchronous.** After registering a brand-new
  person, `ProcessPhotoFunction` triggers training and polls for completion
  (up to 30s) before returning. If training doesn't finish in time, that
  person may not be identifiable until the next training cycle — this is
  logged as a warning rather than treated as a hard failure.
- **Upload endpoint auth** defaults to Azure Functions key-based auth
  (`AuthorizationLevel.Function`). Add Azure AD/APIM in front of it for
  production-grade user authentication.
- **No front-end UI** is included; "users upload photos" is satisfied via
  the HTTP `POST /api/photos` endpoint (curl/Postman/any HTTP client).
- **Poison/failed messages** are dead-lettered as JSON files to a
  `poison-messages` blob container (with the original event + exception
  details) rather than retried indefinitely, since Event Hub triggers have
  no built-in poison-message queue.
- **Face API regional/access availability**: Face API `Identify`/
  `PersonGroup` features require Microsoft's Limited Access approval in some
  subscriptions/regions — apply before relying on this in production.