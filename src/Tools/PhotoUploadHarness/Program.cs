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

using FrsAiDemo.PhotoUploadHarness;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var options = new HarnessOptions();
configuration.Bind(options);

if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
{
    Console.Error.WriteLine($"BaseUrl '{options.BaseUrl}' is not a valid absolute URL.");
    return 1;
}

if (options.RequiresEntraAuthentication && string.IsNullOrWhiteSpace(options.EntraClientId))
{
    Console.Error.WriteLine("EntraClientId is required when BaseUrl targets a non-local Function App.");
    return 1;
}

if (string.IsNullOrWhiteSpace(options.FolderPath) || !Directory.Exists(options.FolderPath))
{
    Console.Error.WriteLine($"FolderPath '{options.FolderPath}' was not found. Set it in appsettings.json or pass --FolderPath=<path>.");
    return 1;
}

var files = Directory.EnumerateFiles(options.FolderPath)
    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
    .Select(f => new FileInfo(f))
    .ToList();

if (files.Count == 0)
{
    Console.Error.WriteLine($"No .jpg/.jpeg/.png files found in '{options.FolderPath}'.");
    return 1;
}

Console.WriteLine($"Found {files.Count} image(s) in '{options.FolderPath}'. Mode: {options.Mode}, target: {options.BaseUrl}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("Cancellation requested, finishing current upload...");
    cts.Cancel();
};

using var httpClient = new HttpClient();
var uploader = new PhotoUploaderClient(httpClient, options);
var verifier = options.EnableVerification ? new ResultVerifier(options) : null;

var successCount = 0;
var failureCount = 0;

try
{
    if (options.IsContinuous)
    {
        var iteration = 0;
        while (!cts.IsCancellationRequested && (options.MaxIterations <= 0 || iteration < options.MaxIterations))
        {
            var file = files[iteration % files.Count];
            await ProcessFileAsync(file, uploader, verifier, cts.Token);
            iteration++;

            if (options.MaxIterations > 0 && iteration >= options.MaxIterations)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), cts.Token);
        }
    }
    else
    {
        foreach (var file in files)
        {
            if (cts.IsCancellationRequested)
            {
                break;
            }

            await ProcessFileAsync(file, uploader, verifier, cts.Token);
        }
    }
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C.
}

Console.WriteLine($"Done. Success: {successCount}, Failures: {failureCount}");
return failureCount > 0 ? 1 : 0;

async Task ProcessFileAsync(FileInfo file, PhotoUploaderClient uploaderClient, ResultVerifier? resultVerifier, CancellationToken cancellationToken)
{
    var result = await uploaderClient.UploadAsync(file, cancellationToken);
    if (!result.Success)
    {
        failureCount++;
        Console.WriteLine($"[FAIL] {result.FileName}: HTTP {result.StatusCode} {result.Error}");
        return;
    }

    successCount++;
    Console.WriteLine($"[OK] {result.FileName} -> uploadId={result.Response?.UploadId} blobUrl={result.Response?.BlobUrl}");

    if (resultVerifier is not null && result.Response is not null)
    {
        var verification = await resultVerifier.WaitForRecognitionAsync(result.Response.BlobUrl, cancellationToken);
        Console.WriteLine(verification.Recognized
            ? $"       recognized personId={verification.PersonId} confidence={verification.Confidence:F2}"
            : "       verification timed out, no matching FaceRecord found");
    }
}
