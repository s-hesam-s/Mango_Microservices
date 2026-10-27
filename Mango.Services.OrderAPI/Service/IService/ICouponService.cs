using Mango.Services.OrderAPI.Models.Dto;

namespace Mango.Services.OrderAPI.Service.IService
{
    public interface ICouponService
    {
        Task<CouponDto?> GetCoupon(string couponCode);
    }
}
