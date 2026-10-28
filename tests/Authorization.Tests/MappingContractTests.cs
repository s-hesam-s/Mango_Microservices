using System.Text.Json;
using Mango.Services.OrderAPI.Models;
using Mango.Services.OrderAPI.Models.Dto;
using Xunit;

namespace Authorization.Tests;

public class MappingContractTests
{
    [Fact]
    public void OrderResponsePreservesStoredPriceAndDetailsWithoutSerializingEntityCycles()
    {
        var header = new OrderHeader { OrderHeaderId = 7, UserId = "owner", OrderTotal = 24.5 };
        header.OrderDetails = new[] { new OrderDetails { OrderDetailsId = 8, OrderHeaderId = 7,
            OrderHeader = header, ProductId = 9, ProductName = "Stored product", Price = 12.25, Count = 2 } };

        var dto = new Mango.Services.OrderAPI.MappingConfig().ToOrderHeaderDto(header);
        var json = JsonSerializer.SerializeToElement(dto);

        Assert.Equal(24.5, json.GetProperty("OrderTotal").GetDouble());
        var detail = json.GetProperty("OrderDetails")[0];
        Assert.Equal("Stored product", detail.GetProperty("ProductName").GetString());
        Assert.Equal(12.25, detail.GetProperty("Price").GetDouble());
        Assert.False(detail.TryGetProperty("OrderHeader", out _));
    }

    [Fact]
    public void CartToOrderMappingUsesProductSnapshotAndPreservesTotals()
    {
        var mapper = new Mango.Services.OrderAPI.MappingConfig();
        var detail = mapper.ToOrderDetailsDto(new CartDetailsDto { ProductId = 9, Count = 2,
            Product = new ProductDto { Name = "Snapshot", Price = 12.25 } });
        var header = mapper.ToOrderHeaderDto(new CartHeaderDto { UserId = "owner", CartTotal = 24.5, Discount = 3 });

        Assert.Equal("Snapshot", detail.ProductName);
        Assert.Equal(12.25, detail.Price);
        Assert.Equal(2, detail.Count);
        Assert.Equal(24.5, header.OrderTotal);
        Assert.Equal(3, header.Discount);
        Assert.Equal("owner", header.UserId);
    }
}
