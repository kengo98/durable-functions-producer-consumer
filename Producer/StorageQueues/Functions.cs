using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Producer.StorageQueues
{
    public static class Functions
    {
        [FunctionName(nameof(PostToStorageQueue))]
        public static async Task<HttpResponseMessage> PostToStorageQueue(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage request,
            [OrchestrationClient]DurableOrchestrationClient client,
            ILogger log)
        {
            var inputObject = JObject.Parse(await request.Content.ReadAsStringAsync());
            var numberOfMessages = inputObject.Value<int>(@"NumberOfMessages");

            var testRunId = Guid.NewGuid().ToString();
            var orchId = await client.StartNewAsync(nameof(GenerateMessagesForStorageQueue),
                    (numberOfMessages, testRunId));

            log.LogTrace($@"Kicked off {numberOfMessages} message creation...");

            return await client.WaitForCompletionOrCreateCheckStatusResponseAsync(request, JsonConvert.SerializeObject(new { TestRunId = testRunId }), TimeSpan.FromMinutes(2));
        }

        [FunctionName(nameof(GenerateMessagesForStorageQueue))]
        public static async Task<bool> GenerateMessagesForStorageQueue(
            [OrchestrationTrigger]DurableOrchestrationContext ctx,
            ILogger log)
        {
            var req = ctx.GetInput<(int numOfMessages, string testRunId)>();

            var activities = Enumerable.Empty<Task<bool>>().ToList();
            for (var i = 0; i < req.numOfMessages; i++)
            {
                try
                {
                    activities.Add(ctx.CallActivityAsync<bool>(nameof(PostMessageToStorageQueue), (i, req.testRunId)));
                }
                catch (Exception ex)
                {
                    log.LogError(ex, @"An error occurred queuing message generation to SB queue");
                    return false;
                }
            }

            return (await Task.WhenAll(activities)).All(r => r);    // return 'true' if all are 'true', 'false' otherwise
        }

        private const int MAX_RETRY_ATTEMPTS = 10;
        private static readonly Lazy<string> _messageContent = new Lazy<string>(() =>
        {
            using (var sr = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream($@"Producer.messagecontent.txt")))
            {
                return sr.ReadToEnd();
            }
        });

        [FunctionName(nameof(PostMessageToStorageQueue))]
        public static async Task<bool> PostMessageToStorageQueue([ActivityTrigger]DurableActivityContext ctx,
            [Queue("%StorageQueueName%", Connection = @"StorageQueueConnection")]IAsyncCollector<JObject> queueMessages,
            ILogger log)
        {
            var msgDetails = ctx.GetInput<(int id, string runId)>();
            var retryCount = 0;
            var retry = false;
            do
            {
                var messageToPost = JObject.FromObject(new
                {
                    Content = _messageContent.Value,
                    EnqueueTimeUtc = DateTime.UtcNow,
                    MessageId = msgDetails.id,
                    TestRunId = msgDetails.runId
                });

                retryCount++;
                try
                {
                    await queueMessages.AddAsync(messageToPost);
                    retry = false;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, $@"Error posting message {messageToPost.Value<int>(@"MessageId")}. Retrying...");
                    retry = true;
                }

                if (retry && retryCount >= MAX_RETRY_ATTEMPTS)
                {
                    log.LogError($@"Unable to post message {messageToPost.Value<int>(@"MessageId")} after {retryCount} attempt(s). Giving up.");
                    break;
                }
                else
                {
#if DEBUG
                    log.LogTrace($@"Posted message {messageToPost.Value<int>(@"MessageId")} (Size: {_messageContent.Value.Length} bytes) in {retryCount} attempt(s)");
#else
                log.LogTrace($@"Posted message in {retryCount} attempt(s)");
#endif
                }
            } while (retry);

            return true;
        }
    }
}
