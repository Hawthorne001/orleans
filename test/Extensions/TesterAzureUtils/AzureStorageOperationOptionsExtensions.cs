using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Options;
using TestExtensions;

namespace Tester.AzureUtils
{
    public static class AzureStorageOperationOptionsExtensions
    {
        public static Orleans.Clustering.AzureStorage.AzureStorageOperationOptions ConfigureTestDefaults(this Orleans.Clustering.AzureStorage.AzureStorageOperationOptions options)
        {
            options.TableServiceClient = GetTableServiceClient();

            return options;
        }

        public static TableServiceClient GetTableServiceClient()
        {
            return TestDefaultConfiguration.UseAadAuthentication
                ? new(TestDefaultConfiguration.TableEndpoint, new DefaultAzureCredential())
                : new(TestDefaultConfiguration.DataConnectionString);
        }

        public static Orleans.GrainDirectory.AzureStorage.AzureStorageOperationOptions ConfigureTestDefaults(this Orleans.GrainDirectory.AzureStorage.AzureStorageOperationOptions options)
        {
            options.TableServiceClient = GetTableServiceClient();

            return options;
        }

        public static Orleans.Persistence.AzureStorage.AzureStorageOperationOptions ConfigureTestDefaults(this Orleans.Persistence.AzureStorage.AzureStorageOperationOptions options)
        {
            options.TableServiceClient = GetTableServiceClient();

            return options;
        }

        public static Orleans.Reminders.AzureStorage.AzureStorageOperationOptions ConfigureTestDefaults(this Orleans.Reminders.AzureStorage.AzureStorageOperationOptions options)
        {
            options.TableServiceClient = GetTableServiceClient();

            return options;
        }

        public static Orleans.Configuration.AzureBlobStorageOptions ConfigureTestDefaults(this Orleans.Configuration.AzureBlobStorageOptions options)
        {
            if (TestDefaultConfiguration.UseAadAuthentication)
            {
                options.BlobServiceClient = new(TestDefaultConfiguration.DataBlobUri, new DefaultAzureCredential());
            }
            else
            {
                options.BlobServiceClient = new(TestDefaultConfiguration.DataConnectionString);
            }

            return options;
        }

        public static Orleans.Configuration.AzureQueueOptions ConfigureTestDefaults(this Orleans.Configuration.AzureQueueOptions options)
        {
            if (TestDefaultConfiguration.UseAadAuthentication)
            {
                options.QueueServiceClient = new(TestDefaultConfiguration.DataQueueUri, new DefaultAzureCredential());
            }
            else
            {
                options.QueueServiceClient = new(TestDefaultConfiguration.DataConnectionString);
            }

            return options;
        }

        public static Orleans.Configuration.AzureBlobLeaseProviderOptions ConfigureTestDefaults(this Orleans.Configuration.AzureBlobLeaseProviderOptions options)
        {
            if (TestDefaultConfiguration.UseAadAuthentication)
            {
                options.BlobServiceClient = new(TestDefaultConfiguration.DataBlobUri, new DefaultAzureCredential());
            }
            else
            {
                options.BlobServiceClient = new(TestDefaultConfiguration.DataConnectionString);
            }

            return options;
        }
    }
}
