using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FrsAiDemo.WebApp.Models;
using FrsAiDemo.WebApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
    public Task<PageResult<FaceRecord>> GetPeopleAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        Task.FromResult(new PageResult<FaceRecord>([], null));

    public Task<FaceRecord?> GetPersonAsync(string personId, CancellationToken cancellationToken) => Task.FromResult<FaceRecord?>(null);
    public Task<UploadRecord?> GetUploadAsync(string uploadId, CancellationToken cancellationToken) => Task.FromResult<UploadRecord?>(null);
    public Task CreateUploadAsync(UploadRecord upload, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetUploadStatusAsync(string uploadId, string status, string? failureSummary, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<ReviewRecord?> GetReviewAsync(string personId, string sightingKey, string reviewerObjectId, CancellationToken cancellationToken) => Task.FromResult<ReviewRecord?>(null);
    public Task<ReviewRecord> UpsertReviewAsync(ReviewInput input, string reviewerObjectId, string reviewerName, CancellationToken cancellationToken) => throw new NotSupportedException();
}