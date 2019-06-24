using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Consumer.ServiceBus
{
    public static class Functions
    {
        [FunctionName(nameof(ServiceBusQueueProcessor))]
        public static void ServiceBusQueueProcessor(
            [ServiceBusTrigger(@"%ServiceBusQueueName%", Connection = @"ServiceBusConnection", IsSessionsEnabled = true)] Message sbMessage,
            ILogger log)
        {
            var timestamp = DateTime.UtcNow;
            log.LogTrace($@"Message received at {timestamp}: {JObject.FromObject(sbMessage).ToString()}");

            var enqueuedTime = sbMessage.ScheduledEnqueueTimeUtc;
            log.LogMetric("messageProcessTimeMs",
                (timestamp - enqueuedTime).TotalMilliseconds,
                new Dictionary<string, object> {
                    { @"Session", sbMessage.SessionId },
                    { @"MessageNo", sbMessage.MessageId },
                    { @"EnqueuedTime", enqueuedTime },
                    { @"DequeuedTime", timestamp }
                });
        }

        [FunctionName(nameof(ClearDeadLetterServiceBusQueue))]
#pragma warning disable IDE0060 // Remove unused parameter
        public static void ClearDeadLetterServiceBusQueue([TimerTrigger("* 0 * * 1", RunOnStartup = true)]TimerInfo myTimer, ILogger log)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            var deadLetterQueueName = $@"{Environment.GetEnvironmentVariable("ServiceBusQueueName")}/$DeadLetterQueue";
            var client = new QueueClient(Environment.GetEnvironmentVariable(@"ServiceBusConnection"),
                    deadLetterQueueName, ReceiveMode.PeekLock);
            client.RegisterMessageHandler((m, cancel) =>
            {
                try
                {
                    // swallow because the MessageHandlerOptions will autocomplete the msg for us
                    log.LogInformation($@"Cleared message {m.MessageId} from Dead Letter queue. Content: {Encoding.Default.GetString(m.Body)}");
                }
                catch (Exception completeEx)
                {
                    // log, but don't worry about, errors
                    log.LogError(completeEx, $@"Encountered an error completing msg in dead letter queue");
                }

                return Task.CompletedTask;
            }, new MessageHandlerOptions(exArgs =>
            {
                log.LogError(exArgs.Exception, $@"Encountered an error completing msg in dead letter queue");
                return Task.CompletedTask;
            })
            { AutoComplete = true });
        }
    }
}
