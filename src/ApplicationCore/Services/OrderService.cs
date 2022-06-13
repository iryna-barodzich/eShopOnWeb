using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
using BlazorShared;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly BaseUrlConfiguration _baseUrlConfiguration;
    private readonly AzureFunctionConfiguration _azureFunctionConfiguration;
    private readonly ServiceBusConfiguration _serviceBusConfiguration;
    private const string _serviceBusFunction = "OrderItemsReserver";
    private const string _httpFunction = "DeliveryOrderProcessor";

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        IOptions<BaseUrlConfiguration> baseUrlConfiguration,
        IOptions<AzureFunctionConfiguration> azureFunctionConfiguration,
        IOptions<ServiceBusConfiguration> serviceBusConfiguration)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
        _baseUrlConfiguration = baseUrlConfiguration.Value;
        _azureFunctionConfiguration = azureFunctionConfiguration.Value;
        _serviceBusConfiguration = serviceBusConfiguration.Value;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.GetBySpecAsync(basketSpec);

        Guard.Against.NullBasket(basketId, basket);
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);
        await PublishNewOrder(order);
        await SendMessage(order);
    }

    public async Task SendMessage(Order order)
    {
        await using var client = new ServiceBusClient(_serviceBusConfiguration.ConnectionString);
        ServiceBusSender sender = client.CreateSender(_serviceBusConfiguration.QueueName);
        string json = JsonSerializer.Serialize(order);
        var message = new ServiceBusMessage(json);
        await sender.SendMessageAsync(message);
    }

    public async Task<string> PublishNewOrder(Order order)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("x-functions-key", _azureFunctionConfiguration.Key);
            string json = JsonSerializer.Serialize(order);
            var data = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{_azureFunctionConfiguration.Url}{_httpFunction}", data);
            var result = await response.Content.ReadAsStringAsync();

            return result;
        }
    }
}
