using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DeliveryOrderProcessor
{
    public class OrderModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        public string BuyerId { get; set; }
        public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.Now;

        public Address ShipToAddress { get; set; }
        public IReadOnlyCollection<OrderItem> OrderItems { get; set; }
    }
}
