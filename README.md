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
                 │  3. Face API Identify│      (Dynamic group: frs-ai-demo-group)
                 │  4. new? create      │
                 │     Person + enroll  │
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

**Why Face API Person Directory/Identify instead of custom vector similarity?**
Azure AI Face API does not expose raw face-embedding vectors through its
public API (a responsible-AI restriction), so a genuinely "custom" vector
similarity search against Cosmos DB isn't possible with this service. The
Face API's own **Person Directory**, **Dynamic Person Group**, and **Identify**
operations do this matching for you server-side. Identify returns candidate
`personId` matches with confidence scores, and Person Directory processes new
enrollments automatically without a Train call. This solution still uses
Cosmos DB to store rich metadata per person (first/last seen and full
recognition history).

## Networking

The Function App still receives **inbound** traffic over the public
internet (the `UploadPhotoFunction` HTTP endpoint). App Service Authentication
validates a single-tenant Microsoft Entra bearer token before forwarding an
upload request to the function. All of the Function App's
**outbound** calls to Storage, Cosmos DB, Event Hub, and the Face API go
over **private networking** instead of the public internet:

- A VNet (`network.bicep`) with two subnets: `snet-integration` (delegated
  to `Microsoft.Web/serverFarms`, used for the Function App's regional VNet
  Integration) and `snet-privateendpoints` (hosts the Private Endpoints).
- A Private Endpoint + Private DNS zone per backend service
  (`privateendpoints.bicep`): Storage (blob/queue/table), Cosmos DB (`Sql`),
  Event Hub namespace (`namespace`), and the Face API (`account`).
- Storage, Cosmos DB, the Event Hub namespace, and the Face API all have
  `publicNetworkAccess: Disabled` — they're reachable only via the Private
  Endpoints, from the Function App's integration subnet.
- The Function App has `virtualNetworkSubnetId` (regional VNet Integration)
  and `vnetRouteAllEnabled: true` so all outbound traffic — not just
  RFC1918 destinations — is routed through the VNet and resolves to the
  private endpoints via the linked Private DNS zones.

**Hosting plan change:** VNet Integration isn't supported on the
Consumption (`Y1`) plan, so the Function App now runs on **Elastic Premium
(`EP1`)**. This is a real cost change (EP1 has a baseline cost even at
zero/low traffic, unlike Consumption's pure pay-per-execution model) —
it's a requirement of private networking, not an incidental choice.

**No Azure Files:** Elastic Premium normally provisions an Azure Files
share for the deployment content, but Azure Files only supports key-based
auth and this storage account has shared-key access disabled. The app
intentionally omits `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING`/
`WEBSITE_CONTENTSHARE` and relies on zip-deploy instead (see ["Create an
app without Azure
Files"](https://learn.microsoft.com/azure/azure-functions/storage-considerations#create-an-app-without-azure-files)) — this can limit scale-out speed but avoids needing a storage key.

**Local development impact:** with `publicNetworkAccess: Disabled` on all
four backend services, your local machine can no longer reach the *real*
deployed resources directly over the internet (this was already blocked on
the storage account by policy; it's now also true for Cosmos DB, Event
Hub, and the Face API, and is intentional rather than incidental). Local
dev continues to rely on Azurite for `AzureWebJobsStorage` — see "Local
development" below. Testing against the real deployed resources now
requires running from inside the VNet (e.g., a jump box/VM on
`snet-integration` or a peered network) or a VPN/ExpressRoute connection
to it.

## Components

| Component | Purpose |
| --- | --- |
| **Blob Storage** (`photos` container) | Stores uploaded photo files. |
| **Event Hub** (`photo-events`) | Carries lightweight upload events (blob URL + metadata) from the upload function to the processing function. |
| **Function App** (.NET 10 isolated) | `UploadPhotoFunction` (HTTP trigger) + `ProcessPhotoFunction` (Event Hub trigger). |
| **Azure AI Face API** | Face detection (`Detect`) and recognition (`Identify` against a Dynamic Person Group); new faces are enrolled in Person Directory without training. |
| **Cosmos DB** (NoSQL API, `Faces` container, partition key `/personId`) | One document per recognized person: `firstSeenUtc`, `lastSeenUtc`, and a `recognitionHistory` array of every sighting (timestamp, blob URL, confidence). |
| **Managed Identity** | Single user-assigned identity used by the Function App to authenticate to Storage, Event Hub, Cosmos DB, and the Face API — no connection strings or keys stored in app settings. |
| **Reviewer Web App** (.NET 10 Razor Pages) | Entra-authenticated review UI for browsing sightings, privately streaming photos, uploading new photos, tracking processing, and recording reviewer decisions. |

## Repository layout

```
/infra/
  main.bicep                 # orchestrates all modules
  modules/
    identity.bicep           # user-assigned managed identity
    network.bicep             # VNet + integration/private-endpoint subnets
    privateendpoints.bicep    # private DNS zones + private endpoints for storage/cosmos/eventhub/face
    storage.bicep            # storage account + "photos" blob container
    eventhub.bicep            # Event Hub namespace + hub + consumer group
    cosmos.bicep              # Cosmos DB account + database + Faces container
    face.bicep                # Azure AI Face API (Cognitive Services) account
    appinsights.bicep         # Log Analytics + Application Insights
    functionapp.bicep         # Function App + Elastic Premium plan + VNet integration + app settings
    rbac.bicep                # role assignments granting the identity access
    webapp.bicep              # reviewer web app + Linux plan + VNet integration + Entra/app settings
    webrbac.bicep             # least-privilege data-plane roles for the web app identity
/src/FunctionApp/
  Program.cs                  # DI setup (DefaultAzureCredential, clients)
  UploadPhotoFunction.cs       # HTTP trigger: upload photo -> blob + event
  ProcessPhotoFunction.cs      # Event Hub trigger: detect/identify/log
  Services/
    BlobStorageService.cs
    FaceApiService.cs          # Face API REST wrapper (Detect/Person Directory/Identify)
    CosmosFaceRepository.cs
  Models/
    PhotoUploadedEvent.cs
    FaceRecord.cs               # Cosmos document + RecognitionEvent
    FaceApiModels.cs            # Face API REST request/response DTOs
  host.json
  local.settings.json.example   # copy to local.settings.json for local dev
/src/WebApp/
  Program.cs                    # Entra auth, Reviewer policy, Azure clients
  Pages/                        # people, sighting review, upload/status, photo proxy
  Services/                     # Cosmos repositories and upload orchestration
```

## Deploying the infrastructure

Prerequisites: [Azure CLI](https://learn.microsoft.com/cli/azure/) with the
Bicep extension (`az bicep install`), and an Azure subscription with access
to create Cognitive Services (Face) resources (this may require approval —
see [Face API limited access](https://learn.microsoft.com/legal/cognitive-services/computer-vision/limited-access-identity)).

The deployment now provisions the reviewer web app alongside the ingestion
pipeline, so it requires the Microsoft Entra app-registration values from
[Microsoft Entra setup](#microsoft-entra-setup). Provide `entraClientId` and
`entraClientSecret`; do not put the secret in a parameters file — pass it as a
secure value at deploy time (or via `entraClientSecret` in Key Vault reference).

| Parameter | Required | Default | Purpose |
| --- | --- | --- | --- |
| `namePrefix` | No | `frsaidemo` | Prefix used to derive all resource names (3–12 lowercase alphanumeric chars). |
| `location` | No | resource group location | Azure region for all resources. |
| `dynamicPersonGroupId` | No | `frs-ai-demo-group` | Face API Dynamic Person Group id used for no-training identification. |
| `entraTenantId` | No | deployment tenant | Microsoft Entra tenant ID for reviewer sign-in. |
| `entraClientId` | **Yes** | — | Client ID of the reviewer web app's Entra registration. |
| `entraClientSecret` | **Yes** (`@secure`) | — | Client secret of the Entra registration. Supply at deploy time; never commit it. |
| `uploadApiClientId` | **Yes** | — | Client ID of the dedicated Entra app registration representing the upload Function API. |

### Upload API Microsoft Entra setup

Create a separate, single-tenant app registration for the upload Function API.
This registration is independent of the reviewer web app registration and does
not change the web app's sign-in or `Reviewer` role:

1. In **Microsoft Entra ID > App registrations**, create a single-tenant
   registration such as `frs-ai-demo-upload-api`.
2. Under **Expose an API**, set the Application ID URI to
   `api://<upload-api-client-id>`.
3. Add a delegated scope named `Photos.Upload`, with users and admins allowed
   to consent.
4. Under **Authorized client applications**, add the Azure CLI application ID
   `04b07795-8ddb-461a-bbee-02f9e1bf7b46` and authorize `Photos.Upload`. This
   allows signed-in Azure CLI users in the tenant to obtain an upload API token
   without a separate client secret.

The Function App's Easy Auth configuration validates both the tenant issuer and
the `api://<upload-api-client-id>` audience and returns HTTP 401 before invoking
the function when the bearer token is absent or invalid. The function's HTTP
trigger is therefore anonymous at the Functions-host layer; do not disable Easy
Auth on the deployed app.

```powershell
az login
az group create --name rg-frs-ai-demo --location eastus

$entraSecret = Read-Host "Entra web app client secret" -AsSecureString
$entraSecretPlain = [System.Net.NetworkCredential]::new('', $entraSecret).Password

az deployment group create `
  --resource-group rg-frs-ai-demo `
  --template-file infra/main.bicep `
  --parameters namePrefix=frsaidemo location=eastus `
               entraClientId=<application-client-id> `
               entraClientSecret=$entraSecretPlain `
               uploadApiClientId=<upload-api-client-id>

Remove-Variable entraSecretPlain
```

Validate templates without deploying (still requires the Entra parameters):

```powershell
az bicep build --file infra/main.bicep --stdout
az deployment group what-if `
  --resource-group rg-frs-ai-demo `
  --template-file infra/main.bicep `
  --parameters entraClientId=<application-client-id> `
               entraClientSecret=<client-secret> `
               uploadApiClientId=<upload-api-client-id>
```

> Deployment takes noticeably longer than a plain PaaS-only setup — Private
> Endpoints, Private DNS zone links, and the Elastic Premium plan warm-up
> each add a few minutes. See [Networking](#networking) above for what's
> being provisioned and its cost/local-dev implications.

After deployment, note the outputs (`functionAppName`, `functionAppHostName`,
`storageAccountName`, `webAppName`, `webAppHostName`, etc.). Use
`functionAppHostName` and the upload API client ID to configure the upload
harness. Add
`https://<webAppHostName>/signin-oidc` as a redirect URI on the Entra app
registration. The Face API resource must have **Limited Access** approval from
Microsoft before the `Identify`/Person Directory operations will work in a
production subscription.

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
   `AzureWebJobsStorage` uses the **Azurite** storage emulator
   (`UseDevelopmentStorage=true`) rather than the real deployed storage
   account or a managed identity. This sidesteps two real-world issues with
   identity-based host storage locally: subscriptions that enforce the
   "prevent shared key access" policy reject connection strings
   (`KeyBasedAuthenticationNotPermitted`), and machines with the **Azure
   Connected Machine Agent** (Azure Arc) installed can make
   `ManagedIdentityCredential` fail hard trying to read an inaccessible Arc
   token file (`Access to the path ...Tokens\*.key is denied`) instead of
   falling back to your `az login` session — and the Functions host has no
   setting to exclude managed identity from its own credential resolution
   for `AzureWebJobsStorage`.
   Install and start Azurite before running the Function App:
```powershell
  npm install -g azurite
   azurite --silent --location .azurite --debug .azurite/debug.log
```
   Leave it running in its own terminal (or a background task) while you
   use `func start`.
2. Ensure you're signed in with `az login` (or have Visual Studio/VS Code
   Azure credentials cached) — `DefaultAzureCredential` will fall back to
   these for local runs instead of the Function App's managed identity, for
   everything other than `AzureWebJobsStorage` (Photos container, Cosmos
   DB, Face API). Your signed-in identity needs **Storage Blob Data
   Contributor** on the storage account for the Photos container:
```powershell
  az role assignment create --assignee <your-user-or-object-id> `
     --role "Storage Blob Data Contributor" `
     --scope <storageAccountResourceId>
```
3. Install [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
   (`func`) and run:
```powershell
  cd src/FunctionApp
   func start
```
   > **Known issue on machines with Azure Arc installed:** the
   > `ProcessPhotoFunction` Event Hub trigger is also an identity-based
   > connection (`EventHub__fullyQualifiedNamespace`), and there's no
   > `EventHub__credential` setting to exclude managed identity the way
   > `Program.cs` does for its own `DefaultAzureCredential`. If your Event
   > Hub namespace also has local/SAS auth disabled by policy
   > (`LocalAuthDisabled`), a plain connection string won't work either. In
   > that case the listener will fail to start with the same
   > `ManagedIdentityCredential authentication failed ... Tokens\*.key is
   > denied`error as`AzureWebJobsStorage\` did. There's no Event Hub
   > emulator equivalent to Azurite without Docker (see the [Azure Event
   > Hubs emulator](https://learn.microsoft.com/azure/event-hubs/overview-emulator)),
   > so the only fixes are on the machine itself: stop the Azure Connected
   > Machine Agent's `himds` service while doing local Functions dev, or
   > add your Windows user to its **"Hybrid Agent Extension Applications"**
   > local group so it can read the token file — both require an elevated
   > PowerShell session.
4. Upload a test photo:
```powershell
  curl -X POST "http://localhost:7071/api/photos" `
     -H "Content-Type: image/jpeg" `
     --data-binary "@C:\path\to\photo.jpg"
```

## Local upload harness

`src/Tools/PhotoUploadHarness` is a standalone .NET 10 console app for
bulk-testing the pipeline from a desktop without `curl`. It posts every
`.jpg`/`.jpeg`/`.png` file in a folder to `UploadPhotoFunction`, either once
(`batch`) or on a repeating interval (`continuous`, simulating a live camera
feed), and can optionally poll Cosmos DB to confirm each photo was
recognized end-to-end.

```powershell
az login --tenant <tenant-id>
cd src/Tools/PhotoUploadHarness
Copy-Item appsettings.json.example appsettings.json
# For Azure, set BaseUrl, EntraClientId, and FolderPath in appsettings.json.
dotnet run
```

Key options (in `appsettings.json`, or overridden via `--Key=Value` args):

| Option | Purpose |
| --- | --- |
| `BaseUrl` | Upload endpoint; defaults to local `func start` (`http://localhost:7071/api/photos`). For Azure, use `https://<functionAppHostName>/api/photos`. |
| `EntraClientId` | Client ID of the upload API app registration. Required for a non-local `BaseUrl`; ignored for localhost. |
| `FolderPath` | Folder of images to upload. |
| `Mode` | `batch` (upload each file once) or `continuous` (loop with `IntervalSeconds` delay, Ctrl+C to stop). |
| `MaxIterations` | Continuous mode only; `0` loops until cancelled. |
| `EnableVerification` | If `true`, polls the `Faces` Cosmos container (`DefaultAzureCredential`) for each upload's recognition result, up to `VerificationTimeoutSeconds`. Requires deployed Cosmos DB access. |

For a deployed endpoint, the harness requests
`api://<EntraClientId>/.default` with its developer credential chain (including
the signed-in Azure CLI identity) and sends the resulting bearer token. It never
stores or logs the token. Uploading works from an internet-connected outside
source because Function App ingress remains public. `EnableVerification=true`
is different: Cosmos DB has public network access disabled, so direct
verification still requires VPN/ExpressRoute or execution inside the VNet, plus
Cosmos data-plane permissions for the signed-in identity.

## Synthetic face SDK benchmark

`src/Tools/FaceBenchmark` generates a versioned synthetic face-verification
library and evaluates Azure Face or vendor-neutral CSV results. The evaluation
set contains 20 identities with one enrollment image and probes targeting
normalized match levels from 95% to 55%. Reserved identities calibrate each
SDK's raw score scale before held-out evaluation.

See [`benchmark/README.md`](benchmark/README.md) for the customer-facing test
protocol, metric definitions, generation and Azure commands, CSV contract,
pass criteria, limitations, and result-submission checklist. Generated images,
model weights, calibration files, and reports are local artifacts and are not
committed.

## Reviewer web app

The .NET 10 Razor Pages app in `src/WebApp` requires single-tenant Microsoft
Entra ID authentication. Every page is protected by a `Reviewer` app-role
policy except `/health`, which is anonymous for App Service health probes.
The browser never receives Cosmos credentials, raw private blob URLs, SAS
tokens, or the Function key.

The web app supports:

- continuation-token paging through face records;
- person details and recognition history;
- authenticated server-side streaming of source photos from private Blob
  Storage;
- photo-level `Correct`, `Incorrect`, or `Unsure` decisions with optional
  reviewer notes;
- JPEG/PNG uploads up to 6 MB directly to Blob Storage and Event Hub using
  managed identity; and
- durable upload states: `Queued`, `Processing`, `Completed`, `NoFaces`, or
  `Failed`, including the explicit `No face data observed` no-face result.

### Microsoft Entra setup

Create a single-tenant app registration before deploying:

1. In **Microsoft Entra ID > App registrations**, create a web application
   for accounts in this organizational directory only.
2. Add `https://localhost:5001/signin-oidc` as an initial Web redirect URI.
3. Add an app role with display name/value `Reviewer`, allowed member type
   `Users/Groups`, a new GUID, and `isEnabled: true`.
4. Leave **Implicit grant and hybrid flows > ID tokens** disabled. The app
  uses OpenID Connect authorization code flow (`response_type=code`).
5. Under the corresponding Enterprise Application, set **Assignment
   required?** to **Yes**, then assign reviewers or a reviewer group to the
   `Reviewer` role.
6. Create a client secret and retain its value only for deployment and local
   user-secrets configuration. Do not add it to JSON or source control.

After the first infrastructure deployment, add
`https://<webAppHostName>/signin-oidc` to the app registration's Web redirect
URIs. The deployed host name is returned by the `webAppHostName` Bicep output.

### Local configuration

Use user secrets for Entra values:

```powershell
cd src/WebApp
dotnet user-secrets set "AzureAd:TenantId" "<tenant-id>"
dotnet user-secrets set "AzureAd:ClientId" "<application-client-id>"
dotnet user-secrets set "AzureAd:ClientSecret" "<client-secret>"
dotnet run --launch-profile https
```

Set the Cosmos, Storage, and Event Hub values in user secrets as needed. The
deployed services have public network access disabled, so a local web process
can reach them only through VPN/ExpressRoute or from a machine connected to
the VNet. `DefaultAzureCredential` excludes managed identity locally to avoid
the Azure Arc credential issue described above.

### Deploying the web app

The Bicep deployment adds a dedicated Linux B1 App Service plan, web app,
user-assigned identity, VNet integration subnet, diagnostic settings, and
least-privilege data-plane roles. Supply the Entra client values at deployment
time; the secret is a secure Bicep parameter and must not be committed to a
parameters file.

```powershell
$entraSecret = Read-Host "Entra web app client secret" -AsSecureString
$entraSecretPlain = [System.Net.NetworkCredential]::new('', $entraSecret).Password

az deployment group create `
  --resource-group rg-frs-ai-demo `
  --template-file infra/main.bicep `
  --parameters namePrefix=frsaidemo location=eastus `
               entraClientId=<application-client-id> `
               entraClientSecret=$entraSecretPlain `
               uploadApiClientId=<upload-api-client-id>

Remove-Variable entraSecretPlain

dotnet publish src/WebApp/FrsAiDemo.WebApp.csproj -c Release -o src/WebApp/publish

# Pack with forward-slash entry names. PowerShell's Compress-Archive can emit backslash
# separators (e.g. runtimes\win\...) that Kudu's Linux zip extractor rejects, so the
# deployment fails at "Extract zip". Build the package with System.IO.Compression instead.
$src = (Resolve-Path src/WebApp/publish).Path
$zip = Join-Path (Resolve-Path src/WebApp).Path 'publish.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$fs = [System.IO.File]::Open($zip, [System.IO.FileMode]::CreateNew)
$archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($f in Get-ChildItem $src -Recurse -File) {
  $name = $f.FullName.Substring($src.Length + 1) -replace '\\','/'
  $entry = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
  $out = $entry.Open()
  $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
  $out.Write($bytes, 0, $bytes.Length); $out.Dispose()
}
$archive.Dispose(); $fs.Dispose()

az webapp deploy --resource-group rg-frs-ai-demo --name <webAppName> `
  --src-path src/WebApp/publish.zip --type zip
```

> **Zip deploy fails at "Extract zip" or returns `Status Code: 400`?** Two
> distinct causes, both seen with this app:
>
> 1. **Bad zip entry separators (most common).** A `publish.zip` built with
>    `Compress-Archive` can contain backslash-separated entries that Kudu's
>    Linux extractor can't unpack — the deployment log shows *"Deployment
>    Failed ... ZipDeploy. Extract zip"*, and OneDeploy returns an empty
>    `400`. Use the `System.IO.Compression` packing shown above (verify with
>    `([IO.Compression.ZipFile]::OpenRead((Resolve-Path src/WebApp/publish.zip)).Entries | ? FullName -like '*\*').Count` returning `0`).
> 2. **SCM basic auth disabled.** `az webapp deploy` uses SCM basic auth by
>    default, but the `scm`/`ftp` `basicPublishingCredentialsPolicies` are
>    often `allow: false` (org Azure Policy). Temporarily allow it for the
>    deployment and turn it back off afterward:
>
>    ```powershell
>    az resource update -g rg-frs-ai-demo --namespace Microsoft.Web `
>      --parent sites/<webAppName> --resource-type basicPublishingCredentialsPolicies `
>      -n scm --set properties.allow=true
>    # ...run az webapp deploy...
>    az resource update -g rg-frs-ai-demo --namespace Microsoft.Web `
>      --parent sites/<webAppName> --resource-type basicPublishingCredentialsPolicies `
>      -n scm --set properties.allow=false
>    ```

The web identity receives Cosmos read access to `Faces`, contributor access to
`Uploads` and `Reviews`, Blob Data Contributor on the private `photos`
container, and Event Hubs Data Sender on `photo-events`. It receives no Face
API permissions and does not reuse the ingestion identity.

## Building / validating

```powershell
# .NET Function App
cd src/FunctionApp
dotnet build

# Photo upload harness
cd src/Tools/PhotoUploadHarness
dotnet build

# Synthetic face benchmark
uv sync --project src/Tools/FaceBenchmark
uv run --project src/Tools/FaceBenchmark python -m unittest discover -s src/Tools/FaceBenchmark/tests -t src/Tools/FaceBenchmark -v

# Complete mixed-target solution and tests (.NET 10 SDK)
dotnet build FrsAiDemo.slnx
dotnet test tests/PhotoUploadHarness.Tests/FrsAiDemo.PhotoUploadHarness.Tests.csproj
dotnet test tests/WebApp.Tests/FrsAiDemo.WebApp.Tests.csproj

# Bicep templates
az bicep build --file infra/main.bicep --stdout
```

## Known limitations / follow-ups

- **Person Directory is a preview API.** The pipeline uses Face API
  `v1.2-preview.1` and a Dynamic Person Group so enrollment is processed
  automatically without Train calls. It polls each asynchronous person/face
  enrollment before completing the upload.
- **Legacy recognition records are review-only.** Cosmos records produced by
  the previous trained PersonGroup flow remain visible, but new duplicate
  matching starts with identities enrolled in Person Directory after this
  deployment.
- **Upload authorization is tenant-wide.** Easy Auth requires a valid token for
  the configured upload API audience and tenant, but does not currently require
  an app role or group claim. Any tenant identity authorized to request the
  delegated upload scope can call the endpoint.
- **Reviews are photo-level.** Existing recognition records do not contain a
  stable sighting ID or face rectangle, so multi-face photos do not highlight
  the specific detected face. Review keys are deterministically derived from
  the existing person, timestamp, blob URL, face ID, and reviewer object ID.
- **Poison/failed messages** are dead-lettered as JSON files to a
  `poison-messages` blob container (with the original event + exception
  details) rather than retried indefinitely, since Event Hub triggers have
  no built-in poison-message queue.
- **Face API regional/access availability**: Face API `Identify` and Person
  Directory features require Microsoft's Limited Access approval in some
  subscriptions/regions — apply before relying on this in production.