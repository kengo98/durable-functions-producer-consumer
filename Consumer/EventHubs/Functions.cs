using System;
using System.Collections.Generic;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Consumer.EventHubs
{
    public static class Functions
    {
        [FunctionName(nameof(EventHubProcessor))]
        public static void EventHubProcessor(
            [EventHubTrigger(@"%EventHubName%", Connection = @"EventHubConnection")] EventData[] ehMessages,
            ILogger log)
        {
            var timestamp = DateTime.UtcNow;
            foreach (var ehMessage in ehMessages)
            {
                // replace 'body' property so output isn't ridiculous
                var jsonMessage = JObject.FromObject(ehMessage);
                jsonMessage.Remove(@"Body");
                jsonMessage.Add(@"Body", $@"{ehMessage.Body.Count} byte(s)");

                var enqueuedTime = (DateTime)ehMessage.Properties[@"EnqueueTimeUtc"];
                var elapsedTime = (timestamp - enqueuedTime).TotalMilliseconds;

                jsonMessage.Add(@"_elapsedTimeMs", elapsedTime);

                log.LogTrace($@"[{ehMessage.Properties[@"TestRunId"]}]: Message received at {timestamp}: {jsonMessage.ToString()}");

                log.LogMetric("messageProcessTimeMs",
                    elapsedTime,
                    new Dictionary<string, object> {
                        { @"PartitionId", ehMessage.Properties[@"PartitionId"] },
                        { @"MessageId", ehMessage.Properties[@"MessageId"] },
                        { @"SystemEnqueuedTime", enqueuedTime },
                        { @"ClientEnqueuedTime", enqueuedTime },
                        { @"DequeuedTime", timestamp }
                    });
            }
        }
    }
}
