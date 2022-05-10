using System.Text.Json.Serialization;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Represents a snapshot of the item that was ordered. If catalog item details change, details of
/// the item that was part of a completed order should not change.
/// </summary>
public class CatalogItemOrdered // ValueObject
{
    public CatalogItemOrdered(int id, string productName, string pictureUri)
    {
        Guard.Against.OutOfRange(id, nameof(id), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(productName, nameof(productName));
        Guard.Against.NullOrEmpty(pictureUri, nameof(pictureUri));

        Id = id;
        ProductName = productName;
        PictureUri = pictureUri;
    }

    private CatalogItemOrdered()
    {
        // required by EF
    }
    [JsonPropertyName("id")]
    public int Id { get; private set; }
    public string ProductName { get; private set; }
    public string PictureUri { get; private set; }
}
