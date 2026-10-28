using Mango.Services.CouponAPI.Models;
using Mango.Services.CouponAPI.Models.Dto;

namespace Mango.Services.CouponAPI;

public sealed class MappingConfig
{
    public Coupon ToCoupon(CouponDto source)
    {
        if (source is null) return null!;
        return new Coupon
        {
            CouponId = source.CouponId,
            CouponCode = source.CouponCode,
            DiscountAmount = source.DiscountAmount,
            MinAmount = source.MinAmount,
        };
    }

    public CouponDto ToCouponDto(Coupon source)
    {
        if (source is null) return null!;
        return new CouponDto
        {
            CouponId = source.CouponId,
            CouponCode = source.CouponCode,
            DiscountAmount = source.DiscountAmount,
            MinAmount = source.MinAmount,
        };
    }

    public List<Coupon> ToCouponList(IEnumerable<CouponDto> source) =>
        source?.Select(ToCoupon).ToList() ?? new List<Coupon>();

    public List<CouponDto> ToCouponDtoList(IEnumerable<Coupon> source) =>
        source?.Select(ToCouponDto).ToList() ?? new List<CouponDto>();
}
