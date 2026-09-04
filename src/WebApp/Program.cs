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

using Azure.Core;
using Azure.Identity;
using Azure.Messaging.EventHubs.Producer;
using Azure.Storage.Blobs;
using FrsAiDemo.WebApp.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.Cosmos;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

// Avoid requiring the app registration's implicit ID-token grant.
builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.ResponseType = OpenIdConnectResponseType.Code;
});

builder.Services.AddAuthorization(options =>
{
    var reviewerPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole("Reviewer")
        .Build();
    options.AddPolicy("Reviewer", reviewerPolicy);
    options.FallbackPolicy = reviewerPolicy;
});

builder.Services.AddRazorPages().AddMicrosoftIdentityUI();
builder.Services.AddApplicationInsightsTelemetry();

var managedIdentityClientId = builder.Configuration["AZURE_CLIENT_ID"];
TokenCredential credential = string.IsNullOrWhiteSpace(managedIdentityClientId)
    ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })
    : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });
builder.Services.AddSingleton(credential);

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var endpoint = configuration["CosmosDb:Endpoint"]
        ?? throw new InvalidOperationException("CosmosDb:Endpoint configuration is required.");
    return new CosmosClient(endpoint, credential, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    });
});

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var accountName = configuration["PhotosStorageAccountName"]
        ?? throw new InvalidOperationException("PhotosStorageAccountName configuration is required.");
    return new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net"), credential);
});

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var fullyQualifiedNamespace = configuration["EventHub:FullyQualifiedNamespace"]
        ?? throw new InvalidOperationException("EventHub:FullyQualifiedNamespace configuration is required.");
    var eventHubName = configuration["EventHub:Name"]
        ?? throw new InvalidOperationException("EventHub:Name configuration is required.");
    return new EventHubProducerClient(fullyQualifiedNamespace, eventHubName, credential);
});

builder.Services.AddSingleton<IFaceReviewRepository, CosmosFaceReviewRepository>();
builder.Services.AddSingleton<IUploadService, UploadService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapControllers();
app.MapRazorPages();
app.Run();

public partial class Program;