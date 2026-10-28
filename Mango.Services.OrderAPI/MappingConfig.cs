using Mango.Services.OrderAPI.Models;
using Mango.Services.OrderAPI.Models.Dto;

namespace Mango.Services.OrderAPI;

public sealed class MappingConfig
{
    public CartHeaderDto ToCartHeaderDto(OrderHeaderDto source)
    {
        if (source is null) return null!;
        return new CartHeaderDto
        {
            UserId = source.UserId,
            CouponCode = source.CouponCode,
            Discount = source.Discount,
            Name = source.Name,
            Phone = source.Phone,
            Email = source.Email,
            CartTotal = source.OrderTotal,
        };
    }

    public OrderHeaderDto ToOrderHeaderDto(CartHeaderDto source)
    {
        if (source is null) return null!;
        return new OrderHeaderDto
        {
            UserId = source.UserId,
            CouponCode = source.CouponCode,
            Discount = source.Discount,
            Name = source.Name,
            Phone = source.Phone,
            Email = source.Email,
            OrderTotal = source.CartTotal,
        };
    }

    public OrderDetailsDto ToOrderDetailsDto(CartDetailsDto source)
    {
        if (source is null) return null!;
        return new OrderDetailsDto
        {
            ProductId = source.ProductId,
            Product = source.Product,
            Count = source.Count,
            ProductName = source.Product?.Name,
            Price = source.Product?.Price ?? 0,
        };
    }

    public CartDetailsDto ToCartDetailsDto(OrderDetailsDto source)
    {
        if (source is null) return null!;
        return new CartDetailsDto
        {
            ProductId = source.ProductId,
            Product = source.Product,
            Count = source.Count,
        };
    }

    public OrderHeaderDto ToOrderHeaderDto(OrderHeader source)
    {
        if (source is null) return null!;
        return new OrderHeaderDto
        {
            OrderHeaderId = source.OrderHeaderId,
            UserId = source.UserId,
            CouponCode = source.CouponCode,
            Discount = source.Discount,
            OrderTotal = source.OrderTotal,
            Name = source.Name,
            Phone = source.Phone,
            Email = source.Email,
            OrderTime = source.OrderTime,
            Status = source.Status,
            PaymentIntentId = source.PaymentIntentId,
            StripeSessionId = source.StripeSessionId,
            OrderDetails = source.OrderDetails?.Select(ToOrderDetailsDto).ToList() ?? new List<OrderDetailsDto>(),
        };
    }

    public OrderHeader ToOrderHeader(OrderHeaderDto source)
    {
        if (source is null) return null!;
        return new OrderHeader
        {
            OrderHeaderId = source.OrderHeaderId,
            UserId = source.UserId,
            CouponCode = source.CouponCode,
            Discount = source.Discount,
            OrderTotal = source.OrderTotal,
            Name = source.Name,
            Phone = source.Phone,
            Email = source.Email,
            OrderTime = source.OrderTime,
            Status = source.Status,
            PaymentIntentId = source.PaymentIntentId,
            StripeSessionId = source.StripeSessionId,
            OrderDetails = source.OrderDetails?.Select(ToOrderDetails).ToList() ?? new List<OrderDetails>(),
        };
    }

    public OrderDetails ToOrderDetails(OrderDetailsDto source)
    {
        if (source is null) return null!;
        return new OrderDetails
        {
            OrderDetailsId = source.OrderDetailsId,
            OrderHeaderId = source.OrderHeaderId,
            ProductId = source.ProductId,
            Product = source.Product,
            Count = source.Count,
            ProductName = source.ProductName,
            Price = source.Price,
        };
    }

    public OrderDetailsDto ToOrderDetailsDto(OrderDetails source)
    {
        if (source is null) return null!;
        return new OrderDetailsDto
        {
            OrderDetailsId = source.OrderDetailsId,
            OrderHeaderId = source.OrderHeaderId,
            ProductId = source.ProductId,
            Product = source.Product,
            Count = source.Count,
            ProductName = source.ProductName,
            Price = source.Price,
        };
    }

    public List<CartHeaderDto> ToCartHeaderDtoList(IEnumerable<OrderHeaderDto> source) =>
        source?.Select(ToCartHeaderDto).ToList() ?? new List<CartHeaderDto>();

    public List<OrderHeaderDto> ToOrderHeaderDtoList(IEnumerable<CartHeaderDto> source) =>
        source?.Select(ToOrderHeaderDto).ToList() ?? new List<OrderHeaderDto>();

    public List<OrderDetailsDto> ToOrderDetailsDtoList(IEnumerable<CartDetailsDto> source) =>
        source?.Select(ToOrderDetailsDto).ToList() ?? new List<OrderDetailsDto>();

    public List<CartDetailsDto> ToCartDetailsDtoList(IEnumerable<OrderDetailsDto> source) =>
        source?.Select(ToCartDetailsDto).ToList() ?? new List<CartDetailsDto>();

    public List<OrderHeaderDto> ToOrderHeaderDtoList(IEnumerable<OrderHeader> source) =>
        source?.Select(ToOrderHeaderDto).ToList() ?? new List<OrderHeaderDto>();

    public List<OrderHeader> ToOrderHeaderList(IEnumerable<OrderHeaderDto> source) =>
        source?.Select(ToOrderHeader).ToList() ?? new List<OrderHeader>();

    public List<OrderDetails> ToOrderDetailsList(IEnumerable<OrderDetailsDto> source) =>
        source?.Select(ToOrderDetails).ToList() ?? new List<OrderDetails>();

    public List<OrderDetailsDto> ToOrderDetailsDtoList(IEnumerable<OrderDetails> source) =>
        source?.Select(ToOrderDetailsDto).ToList() ?? new List<OrderDetailsDto>();
}
