using Mango.Services.ShoppingCartAPI.Models.Dto;
using Mango.Services.ShoppingCartAPI.Models;

namespace Mango.Services.ShoppingCartAPI;

public sealed class MappingConfig
{
    public CartHeaderDto ToCartHeaderDto(CartHeader source)
    {
        if (source is null) return null!;
        return new CartHeaderDto
        {
            CartHeaderId = source.CartHeaderId,
            UserId = source.UserId,
            CouponCode = source.CouponCode,
            Discount = source.Discount,
            CartTotal = source.CartTotal,
        };
    }

    public CartHeader ToCartHeader(CartHeaderDto source)
    {
        if (source is null) return null!;
        return new CartHeader
        {
            CartHeaderId = source.CartHeaderId,
            UserId = source.UserId,
            CouponCode = source.CouponCode,
            Discount = source.Discount,
            CartTotal = source.CartTotal,
        };
    }

    public CartDetailsDto ToCartDetailsDto(CartDetails source)
    {
        if (source is null) return null!;
        return new CartDetailsDto
        {
            CartDetailsId = source.CartDetailsId,
            CartHeaderId = source.CartHeaderId,
            CartHeader = ToCartHeaderDto(source.CartHeader),
            ProductId = source.ProductId,
            Product = source.Product,
            Count = source.Count,
        };
    }

    public CartDetails ToCartDetails(CartDetailsDto source)
    {
        if (source is null) return null!;
        return new CartDetails
        {
            CartDetailsId = source.CartDetailsId,
            CartHeaderId = source.CartHeaderId,
            CartHeader = ToCartHeader(source.CartHeader),
            ProductId = source.ProductId,
            Product = source.Product,
            Count = source.Count,
        };
    }

    public List<CartHeaderDto> ToCartHeaderDtoList(IEnumerable<CartHeader> source) =>
        source?.Select(ToCartHeaderDto).ToList() ?? new List<CartHeaderDto>();

    public List<CartHeader> ToCartHeaderList(IEnumerable<CartHeaderDto> source) =>
        source?.Select(ToCartHeader).ToList() ?? new List<CartHeader>();

    public List<CartDetailsDto> ToCartDetailsDtoList(IEnumerable<CartDetails> source) =>
        source?.Select(ToCartDetailsDto).ToList() ?? new List<CartDetailsDto>();

    public List<CartDetails> ToCartDetailsList(IEnumerable<CartDetailsDto> source) =>
        source?.Select(ToCartDetails).ToList() ?? new List<CartDetails>();
}
