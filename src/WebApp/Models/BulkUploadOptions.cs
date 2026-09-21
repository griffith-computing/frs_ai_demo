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

using System.Text.Json;

namespace FrsAiDemo.WebApp.Models;

public sealed class BulkUploadOptions
{
    public bool Enabled { get; set; }
    public int MaxFiles { get; set; } = 100;
    public int MaxConcurrency { get; set; } = 3;
    public string SourcesJson { get; set; } = "[]";

    public IReadOnlyList<StorageImportSource> GetSources()
    {
        try
        {
            return JsonSerializer.Deserialize<List<StorageImportSource>>(
                SourcesJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("BulkUploads:SourcesJson is not valid JSON.", exception);
        }
    }
}

public sealed class StorageImportSource
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string AccountName { get; init; }
    public required string ContainerName { get; init; }
    public bool AllowCopy { get; init; } = true;
    public bool AllowInPlace { get; init; }
}
