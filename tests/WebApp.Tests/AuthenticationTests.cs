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

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Azure.Core;
using Azure.Messaging.EventHubs.Producer;
using Azure.Storage.Blobs;
using FrsAiDemo.WebApp.Models;
using FrsAiDemo.WebApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrsAiDemo.WebApp.Tests;

public sealed class AuthenticationTests : IClassFixture<ReviewerWebApplicationFactory>
{
    private readonly ReviewerWebApplicationFactory _factory;

    public AuthenticationTests(ReviewerWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_AllowsAnonymousRequests()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task People_RedirectsAnonymousUser()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task People_ForbidsAuthenticatedUserWithoutReviewerRole()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "User");
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task People_AllowsReviewer()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "Reviewer");
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UploadStatus_NoFaces_DisplaysRequiredMessage()
    {
        using var noFacesFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFaceReviewRepository>();
                services.AddSingleton<IFaceReviewRepository>(
                    new EmptyFaceReviewRepository(new UploadRecord
                    {
                        Id = "upload-1",
                        Status = "NoFaces",
                        ContainerName = "photos",
                        BlobName = "upload-1.jpg",
                        BlobUrl = "https://storage.test/photos/upload-1.jpg",
                        ContentType = "image/jpeg",
                        CreatedUtc = DateTimeOffset.UtcNow,
                        UpdatedUtc = DateTimeOffset.UtcNow,
                        DetectedFaceCount = 0
                    }));
            });
        });
        using var client = noFacesFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "Reviewer");

        var body = await client.GetStringAsync("/UploadStatus/upload-1");

        Assert.Contains("No face data observed", body);
    }

    [Fact]
    public async Task UploadService_RejectsImagesOverFaceApiLimit()
    {
        var credential = new StaticTokenCredential();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Uploads:MaxBytes"] = "6291456"
            })
            .Build();
        await using var eventHub = new EventHubProducerClient("test.servicebus.windows.net", "photo-events", credential);
        var service = new UploadService(
            new BlobServiceClient(new Uri("https://storage.test"), credential),
            eventHub,
            new EmptyFaceReviewRepository(),
            configuration);
        var content = new byte[6291457];
        var photo = new FormFile(new MemoryStream(content), 0, content.Length, "Photo", "large.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var exception = await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(
            () => service.UploadAsync(photo, CancellationToken.None));

        Assert.Contains("6 MB", exception.Message);
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("test-token", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) => ValueTask.FromResult(Token);
    }
}

public sealed class ReviewerWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFaceReviewRepository>();
            services.AddSingleton<IFaceReviewRepository, EmptyFaceReviewRepository>();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultForbidScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        });
    }
}

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status302Found;
        Response.Headers.Location = "/signin";
        return Task.CompletedTask;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var role = value.ToString().EndsWith("Reviewer", StringComparison.Ordinal) ? "Reviewer" : "User";
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Role, role),
            new Claim("oid", "test-user-object-id")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

public sealed class EmptyFaceReviewRepository : IFaceReviewRepository
{
    private readonly UploadRecord? _upload;

    public EmptyFaceReviewRepository(UploadRecord? upload = null)
    {
        _upload = upload;
    }

    public Task<PageResult<FaceRecord>> GetPeopleAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        Task.FromResult(new PageResult<FaceRecord>([], null));

    public Task<FaceRecord?> GetPersonAsync(string personId, CancellationToken cancellationToken) => Task.FromResult<FaceRecord?>(null);
    public Task<UploadRecord?> GetUploadAsync(string uploadId, CancellationToken cancellationToken) =>
        Task.FromResult(_upload?.Id == uploadId ? _upload : null);
    public Task CreateUploadAsync(UploadRecord upload, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetUploadStatusAsync(string uploadId, string status, string? failureSummary, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<ReviewRecord?> GetReviewAsync(string personId, string sightingKey, string reviewerObjectId, CancellationToken cancellationToken) => Task.FromResult<ReviewRecord?>(null);
    public Task<ReviewRecord> UpsertReviewAsync(ReviewInput input, string reviewerObjectId, string reviewerName, CancellationToken cancellationToken) => throw new NotSupportedException();
}