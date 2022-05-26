using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using System.Net.Http;
using System.Text;

namespace OrderItemsReserver
{
    public class OrderItemsReserver
    {
        private readonly string _blobConnectionString;
        private readonly string _blobFileContainerName;
        private readonly string _endpointUri;
        private readonly string _primaryKey;
        private readonly string _logicAppUri;

        public OrderItemsReserver(IConfiguration configuration)
        {
            _blobConnectionString = configuration.GetValue<string>("BlobConnectionString");
            _blobFileContainerName = configuration.GetValue<string>("BlobFileContainerName");
            _endpointUri = configuration.GetValue<string>("EndpointUri");
            _primaryKey = configuration.GetValue<string>("PrimaryKey");
            _logicAppUri = configuration.GetValue<string>("LogicAppUrl");
        }

        [FunctionName("OrderItemsReserver")]
        public async Task Run(
            //[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req)
            [ServiceBusTrigger("test-queue", Connection = "ServiceBusConnection")] string myQueueItem,
    Int32 deliveryCount,
    DateTime enqueuedTimeUtc,
    string messageId)
            {

            string requestBody = myQueueItem;

            try
            {
                await UploadToBlob(requestBody);
            }
            catch (Exception ex)
            {
                HttpClient httpClient = new HttpClient();
                var result = await httpClient.PostAsync(_logicAppUri, new StringContent(requestBody, Encoding.UTF8, "application/json"));
            }


            //return new OkObjectResult("Success");
        }

        private async Task<string> UploadToCosmosDb(string json)
        {
            try
            {
                var cosmosOptions = new CosmosClientOptions()
                {
                    SerializerOptions = new CosmosSerializationOptions()
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                    }

                };
                using (CosmosClient client = new CosmosClient(connectionString: _endpointUri, clientOptions: cosmosOptions))
                {
                    DatabaseResponse databaseResponse = await client.CreateDatabaseIfNotExistsAsync("Delivery");
                    Database targetDatabase = databaseResponse.Database;
                    IndexingPolicy indexingPolicy = new IndexingPolicy
                    {
                        IndexingMode = IndexingMode.Consistent,
                        Automatic = true,
                        IncludedPaths =
                    {
                        new IncludedPath
                        {
                            Path = "/*"
                        }
                    }
                    };
                    var containerProperties = new ContainerProperties("Orders", "/Id")
                    {
                        IndexingPolicy = indexingPolicy
                    };
                    var containerResponse = await targetDatabase.CreateContainerIfNotExistsAsync(containerProperties, 1000);
                    var customContainer = containerResponse.Container;
                    var order = JsonConvert.DeserializeObject<Order>(json);
                    var orderModel = new OrderModel
                    {
                        Id = order.Id.ToString(),
                        ShipToAddress = order.ShipToAddress,
                        BuyerId = order.BuyerId,
                        OrderDate = order.OrderDate,
                        OrderItems = order.OrderItems
                    };
                    var test = await customContainer.CreateItemAsync(orderModel);
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            return "Success";
        }

        private async Task UploadToBlob(string json)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(_blobConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            blobClient.DefaultRequestOptions = new BlobRequestOptions()
            {
                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(1), 3)
            };
            CloudBlobContainer blobContainer = blobClient.GetContainerReference(_blobFileContainerName);
            await blobContainer.CreateIfNotExistsAsync();

            CloudBlockBlob blob = blobContainer.GetBlockBlobReference(Guid.NewGuid().ToString());

            blob.Properties.ContentType = "application/json";

            using (var ms = new MemoryStream())
            {
                LoadStreamWithJson(ms, json);
                await blob.UploadFromStreamAsync(ms);
            }
            await blob.SetPropertiesAsync();
        }

        private void LoadStreamWithJson(Stream ms, object obj)
        {
            StreamWriter writer = new StreamWriter(ms);
            writer.Write(obj);
            writer.Flush();
            ms.Position = 0;
        }
    }
}
