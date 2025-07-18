using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Streaming.SQS;
using SQSMessage = Amazon.SQS.Model.Message;

namespace OrleansAWSUtils.Storage
{
    /// <summary>
    /// Wrapper/Helper class around AWS SQS queue service
    /// </summary>
    internal partial class SQSStorage
    {
        /// <summary>
        /// Maximum number of messages allowed by SQS to peak per request
        /// </summary>
        public const int MAX_NUMBER_OF_MESSAGE_TO_PEEK = 10;
        private const string AccessKeyPropertyName = "AccessKey";
        private const string SecretKeyPropertyName = "SecretKey";
        private const string ServicePropertyName = "Service";
        private readonly ILogger Logger;
        private string accessKey;
        private string secretKey;
        private string service;
        private string queueUrl;
        private AmazonSQSClient sqsClient;

        /// <summary>
        /// The queue Name
        /// </summary>
        public string QueueName { get; private set; }

        /// <summary>
        /// Default Ctor
        /// </summary>
        /// <param name="loggerFactory">logger factory to use</param>
        /// <param name="queueName">The name of the queue</param>
        /// <param name="connectionString">The connection string</param>
        /// <param name="serviceId">The service ID</param>
        public SQSStorage(ILoggerFactory loggerFactory, string queueName, string connectionString, string serviceId = "")
        {
            QueueName = string.IsNullOrWhiteSpace(serviceId) ? queueName : $"{serviceId}-{queueName}";
            ParseDataConnectionString(connectionString);
            Logger = loggerFactory.CreateLogger<SQSStorage>();
            CreateClient();
        }

        private void ParseDataConnectionString(string dataConnectionString)
        {
            var parameters = dataConnectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

            var serviceConfig = Array.Find(parameters, p => p.Contains(ServicePropertyName));
            if (!string.IsNullOrWhiteSpace(serviceConfig))
            {
                var value = serviceConfig.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
                    service = value[1];
            }

            var secretKeyConfig = Array.Find(parameters, p => p.Contains(SecretKeyPropertyName));
            if (!string.IsNullOrWhiteSpace(secretKeyConfig))
            {
                var value = secretKeyConfig.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
                    secretKey = value[1];
            }

            var accessKeyConfig = Array.Find(parameters, p => p.Contains(AccessKeyPropertyName));
            if (!string.IsNullOrWhiteSpace(accessKeyConfig))
            {
                var value = accessKeyConfig.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
                    accessKey = value[1];
            }
        }

        private void CreateClient()
        {
            if (service.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                service.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Local SQS instance (for testing)
                var credentials = new BasicAWSCredentials("dummy", "dummyKey");
                sqsClient = new AmazonSQSClient(credentials, new AmazonSQSConfig { ServiceURL = service });
            }
            else if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
            {
                // AWS SQS instance (auth via explicit credentials)
                var credentials = new BasicAWSCredentials(accessKey, secretKey);
                sqsClient = new AmazonSQSClient(credentials, new AmazonSQSConfig { RegionEndpoint = AWSUtils.GetRegionEndpoint(service) });
            }
            else
            {
                // AWS SQS instance (implicit auth - EC2 IAM Roles etc)
                sqsClient = new AmazonSQSClient(new AmazonSQSConfig { RegionEndpoint = AWSUtils.GetRegionEndpoint(service) });
            }
        }

        private async Task<string> GetQueueUrl()
        {
            try
            {
                var response = await sqsClient.GetQueueUrlAsync(QueueName);
                if (!string.IsNullOrWhiteSpace(response.QueueUrl))
                    queueUrl = response.QueueUrl;

                return queueUrl;
            }
            catch (QueueDoesNotExistException)
            {
                return null;
            }
        }

        /// <summary>
        /// Initialize SQSStorage by creating or connecting to an existent queue
        /// </summary>
        /// <returns></returns>
        public async Task InitQueueAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(await GetQueueUrl()))
                {
                    var response = await sqsClient.CreateQueueAsync(QueueName);
                    queueUrl = response.QueueUrl;
                }
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "InitQueueAsync");
            }
        }

        /// <summary>
        /// Delete the queue
        /// </summary>
        /// <returns></returns>
        public async Task DeleteQueue()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(queueUrl))
                    throw new InvalidOperationException("Queue not initialized");
                await sqsClient.DeleteQueueAsync(queueUrl);
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "DeleteQueue");
            }
        }

        /// <summary>
        /// Add a message to the SQS queue
        /// </summary>
        /// <param name="message">Message request</param>
        /// <returns></returns>
        public async Task AddMessage(SendMessageRequest message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(queueUrl))
                    throw new InvalidOperationException("Queue not initialized");

                message.QueueUrl = queueUrl;
                await sqsClient.SendMessageAsync(message);
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "AddMessage");
            }
        }

        /// <summary>
        /// Get Messages from SQS Queue.
        /// </summary>
        /// <param name="count">The number of messages to peak. Min 1 and max 10</param>
        /// <returns>Collection with messages from the queue</returns>
        public async Task<IEnumerable<SQSMessage>> GetMessages(int count = 1)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(queueUrl))
                    throw new InvalidOperationException("Queue not initialized");

                if (count < 1)
                    throw new ArgumentOutOfRangeException(nameof(count));

                var request = new ReceiveMessageRequest { QueueUrl = queueUrl, MaxNumberOfMessages = count <= MAX_NUMBER_OF_MESSAGE_TO_PEEK ? count : MAX_NUMBER_OF_MESSAGE_TO_PEEK };
                var response = await sqsClient.ReceiveMessageAsync(request);
                return response.Messages;
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "GetMessages");
            }
            return null;
        }

        /// <summary>
        /// Delete a message from SQS queue
        /// </summary>
        /// <param name="message">The message to be deleted</param>
        /// <returns></returns>
        public async Task DeleteMessage(SQSMessage message)
        {
            try
            {
                if (message == null)
                    throw new ArgumentNullException(nameof(message));

                if (string.IsNullOrWhiteSpace(message.ReceiptHandle))
                    throw new ArgumentNullException(nameof(message.ReceiptHandle));

                if (string.IsNullOrWhiteSpace(queueUrl))
                    throw new InvalidOperationException("Queue not initialized");

                await sqsClient.DeleteMessageAsync(
                    new DeleteMessageRequest { QueueUrl = queueUrl, ReceiptHandle = message.ReceiptHandle });
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "DeleteMessage");
            }
        }

        private void ReportErrorAndRethrow(Exception exc, string operation)
        {
            LogErrorSQSOperation(exc, operation, QueueName);
            throw new AggregateException($"Error doing {operation} for SQS queue {QueueName}", exc);
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.StreamProviderManagerBase,
            Level = LogLevel.Error,
            Message = "Error doing {Operation} for SQS queue {QueueName}"
        )]
        private partial void LogErrorSQSOperation(Exception exception, string operation, string queueName);
    }
}
