using System;

namespace Producer.AmazonSqs
{
    internal class GroupMessagesCreateRequest
    {
        public string GroupId { get; set; }
        public int MessageId { get; set; }
        public DateTime EnqueueTimeUtc { get; set; }
        public string TestRunId { get; set; }
    }
}