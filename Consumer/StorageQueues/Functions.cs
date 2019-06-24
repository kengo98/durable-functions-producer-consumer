using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json.Linq;

namespace Consumer.StorageQueues
{
    public static class Functions
    {
        [FunctionName(nameof(StorageQueueProcessor))]
        public static void StorageQueueProcessor(
            [QueueTrigger(@"%StorageQueueName%", Connection = @"StorageQueueConnection")] CloudQueueMessage queueMessage,
            ILogger log)
        {
            var timestamp = DateTime.UtcNow;
            // replace 'body' property so output isn't ridiculous

            var jsonMessage = JObject.FromObject(queueMessage);
            var jsonContent = JObject.Parse(queueMessage.AsString);

            var enqueuedTime = jsonContent.Value<DateTime>(@"EnqueueTimeUtc");
            var elapsedTime = (timestamp - enqueuedTime).TotalMilliseconds;

            jsonMessage.Add(@"_elapsedTimeMs", elapsedTime);

            log.LogTrace($@"Message received at {timestamp}: {jsonMessage.ToString()}");

            log.LogMetric("messageProcessTimeMs",
                elapsedTime,
                new Dictionary<string, object> {
                        { @"MessageId", jsonContent.Value<int>(@"MessageId") },
                        { @"SystemEnqueuedTime", queueMessage.InsertionTime },
                        { @"ClientEnqueuedTime", enqueuedTime },
                        { @"DequeuedTime", timestamp }
                });
        }
    }
}
