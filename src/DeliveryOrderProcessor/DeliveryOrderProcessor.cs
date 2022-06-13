using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.Extensions.Configuration;

namespace DeliveryOrderProcessor
{
    public class DeliveryOrderProcessor
    {
        private readonly string _endpointUri;

        public DeliveryOrderProcessor(IConfiguration configuration)
        {
            _endpointUri = configuration.GetValue<string>("EndpointUri");
        }

        [FunctionName("DeliveryOrderProcessor")]
        public async Task<IActionResult> Run(
            //[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req)
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var responseMessage = await UploadToCosmosDb(requestBody);

            return new OkObjectResult(responseMessage);
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
    }
}
