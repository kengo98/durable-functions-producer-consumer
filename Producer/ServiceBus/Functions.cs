using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Producer.ServiceBus
{
    public static class Functions
    {
        [FunctionName(nameof(PostToServiceBusQueue))]
        public static async Task<HttpResponseMessage> PostToServiceBusQueue(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage request,
            [OrchestrationClient]DurableOrchestrationClient client,
            ILogger log)
        {
            var inputObject = JObject.Parse(await request.Content.ReadAsStringAsync());
            var numberOfMessagesPerSession = inputObject.Value<int>(@"NumberOfMessagesPerSession");
            var numberOfSessions = inputObject.Value<int>(@"NumberOfSessions");

            var orchestrationIds = new List<string>();
            for (var c = 1; c <= numberOfSessions; c++)
            {
                var sessionId = Guid.NewGuid().ToString();
                var orchId = await client.StartNewAsync(nameof(GenerateMessagesForServiceBusSession),
                    new SessionCreateRequest
                    {
                        SessionId = sessionId,
                        NumberOfMessagesPerSession = numberOfMessagesPerSession,
                    });

                log.LogTrace($@"Kicked off message creation for session {sessionId}...");

                orchestrationIds.Add(orchId);
            }

            return await client.WaitForCompletionOrCreateCheckStatusResponseAsync(request, orchestrationIds.First(), TimeSpan.FromMinutes(2));
        }

        [FunctionName(nameof(GenerateMessagesForServiceBusSession))]
        public static async Task<bool> GenerateMessagesForServiceBusSession(
            [OrchestrationTrigger]DurableOrchestrationContext ctx,
            ILogger log)
        {
            var req = ctx.GetInput<SessionCreateRequest>();

            var messages = Enumerable.Range(1, req.NumberOfMessagesPerSession)
                    .Select(m =>
                    {
                        return new SessionMessagesCreateRequest
                        {
                            SessionId = req.SessionId,
                            MessageId = m,
                            EnqueueTimeUtc = DateTime.UtcNow,
                        };
                    }).ToList();

            try
            {
                return await ctx.CallActivityAsync<bool>(nameof(PostMessagesToServiceBusQueue), messages);
            }
            catch (Exception ex)
            {
                log.LogError(ex, @"An error occurred queuing message generation to SB queue");
                return false;
            }
        }

        private static readonly Lazy<byte[]> _messageContent = new Lazy<byte[]>(() =>
        {
            using (var sr = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream($@"Producer.messagecontent.txt")))
            {
                return Encoding.Default.GetBytes(sr.ReadToEnd());
            }
        });

        private const int MAX_RETRY_ATTEMPTS = 10;

        [FunctionName(nameof(PostMessagesToServiceBusQueue))]
        public static async Task<bool> PostMessagesToServiceBusQueue([ActivityTrigger]DurableActivityContext ctx,
            [ServiceBus("%ServiceBusQueueName%", Connection = @"ServiceBusConnection")]IAsyncCollector<Message> queueMessages,
            ILogger log)
        {
            var messages = ctx.GetInput<IEnumerable<SessionMessagesCreateRequest>>();

            foreach (var messageToPost in messages.Select(m => new Message
            {
                Body = _messageContent.Value,
                ContentType = @"text/plain",    // feel free to change this if your content is JSON (application/json), XML (application/xml), etc
                CorrelationId = m.SessionId,
                MessageId = $@"{m.SessionId}/{m.MessageId}",    // this property is used for de-duping
                ScheduledEnqueueTimeUtc = m.EnqueueTimeUtc,
                SessionId = m.SessionId,
            }))
            {
                var retryCount = 0;
                var retry = false;
                do
                {
                    retryCount++;
                    try
                    {
                        await queueMessages.AddAsync(messageToPost);
                        retry = false;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, $@"Error posting message for session '{messageToPost.SessionId}'. Retrying...");
                        retry = true;
                    }

                    if (retry && retryCount >= MAX_RETRY_ATTEMPTS)
                    {
                        log.LogError($@"Unable to post message to {messageToPost.SessionId} after {retryCount} attempt(s). Giving up.");
                        break;
                    }
                    else
                    {
#if DEBUG
                        log.LogTrace($@"Posted message {messageToPost.MessageId} (Size: {messageToPost.Body.Length} bytes) for session '{messageToPost.SessionId}' in {retryCount} attempt(s)");
#else
                log.LogTrace($@"Posted message for session '{messageToPost.SessionId}' in {retryCount} attempt(s)");
#endif
                    }
                } while (retry);
            }

            return true;
        }
    }
}
