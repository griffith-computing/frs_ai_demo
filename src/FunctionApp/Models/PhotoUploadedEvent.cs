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

namespace FrsAiDemo.FunctionApp.Models;

/// <summary>
/// Lightweight event published to Event Hub after a photo is uploaded to Blob Storage.
/// Kept small deliberately since Event Hub is not suited to carrying binary payloads.
/// </summary>
public sealed class PhotoUploadedEvent
{
    public required string UploadId { get; init; }
    public required string BlobUrl { get; init; }
    public required string ContainerName { get; init; }
    public required string BlobName { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
