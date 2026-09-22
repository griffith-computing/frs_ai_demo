using System.Net;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using FaceLab.Core;
using FaceLab.Core.Data;
using FaceLab.Shared.Services;
using FaceLab.Web.Components;
using FaceLab.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FaceLabOptions>(builder.Configuration.GetSection("FaceLab"));
builder.Services.Configure<FaceLabStorageOptions>(builder.Configuration.GetSection("FaceLabStorage"));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddApplicationInsightsTelemetry();

var managedIdentityClientId = builder.Configuration["AZURE_CLIENT_ID"];
TokenCredential credential = string.IsNullOrWhiteSpace(managedIdentityClientId)
    ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })
    : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });
builder.Services.AddSingleton(credential);

builder.Services.AddSingleton(serviceProvider =>
{
    var storageOptions = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<FaceLabStorageOptions>>()
        .Value;
    storageOptions.Validate();

    if (!string.IsNullOrWhiteSpace(storageOptions.ConnectionString))
    {
        return new BlobContainerClient(storageOptions.ConnectionString, storageOptions.ContainerName);
    }

    var accountUri = new Uri($"https://{storageOptions.AccountName}.blob.core.windows.net");
    return new BlobServiceClient(accountUri, credential).GetBlobContainerClient(storageOptions.ContainerName);
});

builder.Services.AddDbContextFactory<FaceLabDbContext>((serviceProvider, options) =>
{
    var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
    var homeDirectory = Environment.GetEnvironmentVariable("HOME");
    var databaseDirectory = !environment.IsDevelopment() && !string.IsNullOrWhiteSpace(homeDirectory)
        ? Path.Combine(homeDirectory, "data")
        : Path.Combine(environment.ContentRootPath, "App_Data");
    Directory.CreateDirectory(databaseDirectory);
    options.UseSqlite($"Data Source={Path.Combine(databaseDirectory, "facelab-web.db")}");
});

var handler = new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    AutomaticDecompression = DecompressionMethods.All
};

builder.Services.AddSingleton<IFaceApiClientFactory>(_ => new FaceApiClientFactory(
    () => new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) },
    () => credential));
builder.Services.AddScoped<IFaceLabRepository, BlobFaceLabRepository>();
builder.Services.AddScoped<IFaceRunner, FaceRunner>();
builder.Services.AddScoped<IFaceLabWorkbench, FaceLabWorkbench>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(FaceLab.Shared._Imports).Assembly);

app.Run();
