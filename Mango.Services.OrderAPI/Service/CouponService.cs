using Mango.Services.OrderAPI.Models.Dto;
using Mango.Services.OrderAPI.Service.IService;
using Newtonsoft.Json;

namespace Mango.Services.OrderAPI.Service
{
    public class CouponService : ICouponService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public CouponService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<CouponDto?> GetCoupon(string couponCode)
        {
            using var response = await _httpClientFactory.CreateClient("Coupon")
                .GetAsync($"/api/coupon/GetByCode/{Uri.EscapeDataString(couponCode)}");
            response.EnsureSuccessStatusCode();
            var envelope = JsonConvert.DeserializeObject<ResponseDto>(await response.Content.ReadAsStringAsync());
            if (envelope == null) throw new HttpRequestException("Invalid coupon service response.");
            return envelope.IsSuccess && envelope.Result != null
                ? JsonConvert.DeserializeObject<CouponDto>(Convert.ToString(envelope.Result)!) : null;
        }
    }
}
