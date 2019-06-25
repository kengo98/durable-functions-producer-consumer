---
author: BrandonH-MSFT
title: Produce & Consume messages through Service Bus, Event Hubs, and Storage Queues with Azure Functions
description: This sample shows how to utilize Durable Functions' fan out pattern to load an arbitrary number of messages across any number of sessions/partitions in to Service Bus, Event Hubs, or Storage Queues. It also adds the ability to consume those messages with another Azure Function and load the resulting timing data in to another Event Hub for ingestion in to analytics services like Azure Data Explorer.
topic: sample
ms.topic: sample
languages:
  - csharp
platforms: dotnet
products:
  - azure-functions
  - azure-event-hubs
  - azure-service-bus
  - azure-storage
services: functions, durablefunctions
---
This sample shows how to utilize Durable Functions' fan out pattern to load an arbitrary number of messages across any number of sessions/partitions in to Service Bus, Event Hubs, or Storage Queues. It also adds the ability to consume those messages with another Azure Function and load the resulting timing data in to another Event Hub for ingestion in to analytics services like Azure Data Explorer.

> **Note**: Please use the `sample.local.settings.json` file as the baseline for `local.settings.json` when testing this sample locally.

## Service Bus
```
POST /api/PostToServiceBusQueue HTTP/1.1
Content-Type: application/json
cache-control: no-cache

{
	"NumberOfSessions": 2,
	"NumberOfMessagesPerSession": 2
}
```
Will post two messages across two sessions to the Service Bus queue specified by the `ServiceBusConnection` and `ServiceBusQueueName` settings in your `local.settings.json` file or - when published to Azure - the Function App's application settings.

## Event Hubs
```
POST /api/PostToEventHub HTTP/1.1
Content-Type: application/json
cache-control: no-cache

{
	"NumberOfPartitions": 2,
	"NumberOfMessagesPerPartition": 2
}
```
Will post two messages across two paritions to the Event Hub specified by the `EventHubConnection` and `EventHubName` settings in your `local.settings.json` file or - when published to Azure - the Function App's application settings.

## Storage Queues
```
POST /api/PostToStorageQueue HTTP/1.1
Content-Type: application/json
cache-control: no-cache

{
	"NumberOfMessages": 2
}
```
Will post two messages to the Storage Queue specified by the `StorageQueueConnection` and `StorageQueueName` settings in your `local.settings.json` file or - when published to Azure - the Function App's application settings.

## Implementation
### Fan out/in
This sample utilizes [Durable Functions fan out/in pattern](https://docs.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-cloud-backup) to produce messages in parallel across the sessions/partitions you specify. For this reason, pay close attention to [the `DFTaskHubName` application setting](Producer/sample.local.settings.json) if you put it in the same Function App as other Durable Function implementation.

### Message content
The content for each message is **not** dynamic at this time. It is simply stored in the [messagecontent.txt](Producer/messagecontent.txt) file and posted as the content for each message in every scenario. If you wish to make the content dynamic, you can do so by changing the code in each scenario's `Functions.cs` file of the `Producer` project.