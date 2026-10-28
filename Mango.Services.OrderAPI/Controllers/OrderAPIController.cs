using AutoMapper;
using Mango.MessageBus;
using Mango.Services.OrderAPI.Data;
using Mango.Services.OrderAPI.Models;
using Mango.Services.OrderAPI.Models.Dto;
using Mango.Services.OrderAPI.Service.IService;
using Mango.Services.OrderAPI.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using System.Security.Claims;

namespace Mango.Services.OrderAPI.Controllers
{
    [Route("api/order")]
    [ApiController]
    [Authorize]
    public class OrderAPIController : ControllerBase
    {
        protected ResponseDto _response;
        private IMapper _mapper;
        private readonly AppDbContext _db;
        private IProductService _productService;
        private readonly ICouponService _couponService;
        private readonly IMessageBus _messageBus;
        private readonly IConfiguration _configuration;
        public OrderAPIController(AppDbContext db, IProductService productService, IMapper mapper, 
                                  IConfiguration configuration, IMessageBus messageBus, ICouponService couponService)
        {
            _db = db;
            this._response = new ResponseDto();
            _productService = productService;
            _couponService = couponService;
            _mapper = mapper;
            _messageBus = messageBus;
            _configuration = configuration;
        }

        [Authorize]
        [HttpGet("GetOrders")]
        public ActionResult<ResponseDto> Get(string? userId = "")
        {
            var currentUserId = getUserId();
            if (currentUserId == null) return Forbid();
            if (!User.IsInRole(SD.RoleAdmin) && !string.IsNullOrEmpty(userId) && userId != currentUserId) return Forbid();
            try
            {
                IEnumerable<OrderHeader> objList;
                if (User.IsInRole(SD.RoleAdmin))
                {
                    objList = _db.OrderHeaders.Include(u => u.OrderDetails).OrderByDescending(u => u.OrderHeaderId).ToList();
                }
                else
                {
                    objList = _db.OrderHeaders.Include(u => u.OrderDetails).Where(u => u.UserId == currentUserId).OrderByDescending(u => u.OrderHeaderId).ToList();
                }
                _response.Result = _mapper.Map<IEnumerable<OrderHeaderDto>>(objList);
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.Message;
            }
            return _response;
        }

        [Authorize]
        [HttpGet("GetOrder/{id:int}")]
        public ActionResult<ResponseDto> Get(int id)
        {
            var currentUserId = getUserId();
            if (currentUserId == null) return Forbid();
            try
            {
                OrderHeader? orderHeader = _db.OrderHeaders.Include(u => u.OrderDetails).FirstOrDefault(u => u.OrderHeaderId == id);
                if (orderHeader == null) return NotFound();
                if (!User.IsInRole(SD.RoleAdmin) && orderHeader.UserId != currentUserId) return Forbid();
                _response.Result = _mapper.Map<OrderHeaderDto>(orderHeader);
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.Message;
            }
            return _response;
        }

        [Authorize]
        [HttpPost("CreateOrder")]
        public async Task<ActionResult<ResponseDto>> CreateOrder([FromBody] CartDto cartDto)
        {
            var currentUserId = getUserId();
            if (currentUserId == null) return Forbid();
            if (cartDto?.CartHeader == null || cartDto.CartDetails == null) return BadRequest();
            if (!string.IsNullOrEmpty(cartDto.CartHeader.UserId) && cartDto.CartHeader.UserId != currentUserId) return Forbid();
            cartDto.CartHeader.UserId = currentUserId;
            try
            {
                var items = cartDto.CartDetails.ToList();
                if (items.Count == 0 || items.Any(i => i == null || i.Count <= 0 || i.ProductId <= 0)
                    || items.Select(i => i.ProductId).Distinct().Count() != items.Count)
                    return failure("An order requires distinct products with positive quantities.", 400);
                var products = (await _productService.GetProducts()).ToDictionary(p => p.ProductId);
                var details = new List<OrderDetails>();
                decimal subtotal = 0;
                foreach (var item in items)
                {
                    if (!products.TryGetValue(item.ProductId, out var product))
                        return failure("An order product is unavailable.", 400);
                    var price = money(product.Price);
                    subtotal = checked(subtotal + price * item.Count);
                    details.Add(new OrderDetails { ProductId = product.ProductId, ProductName = product.Name,
                        Price = (double)price, Count = item.Count });
                }
                decimal discount = 0;
                string? couponCode = null;
                if (!string.IsNullOrWhiteSpace(cartDto.CartHeader.CouponCode))
                {
                    var coupon = await _couponService.GetCoupon(cartDto.CartHeader.CouponCode);
                    if (coupon == null || string.IsNullOrWhiteSpace(coupon.CouponCode))
                        return failure("The coupon is invalid.", 400);
                    if (coupon.MinAmount < 0) throw new InvalidOperationException("Invalid coupon minimum.");
                    var couponAmount = money(coupon.DiscountAmount);
                    if (subtotal >= coupon.MinAmount)
                    {
                        discount = Math.Min(subtotal, couponAmount);
                        couponCode = coupon.CouponCode;
                    }
                }
                var orderCreated = new OrderHeader
                {
                    UserId = currentUserId, Name = cartDto.CartHeader.Name, Phone = cartDto.CartHeader.Phone,
                    Email = cartDto.CartHeader.Email, OrderTime = DateTime.UtcNow, Status = SD.Status_Pending,
                    CouponCode = couponCode, Discount = (double)discount, OrderTotal = (double)(subtotal - discount),
                    OrderDetails = details
                };
                _db.OrderHeaders.Add(orderCreated);
                await _db.SaveChangesAsync();
                _response.Result = _mapper.Map<OrderHeaderDto>(orderCreated);
            }
            catch (Exception)
            {
                return failure("Order pricing is temporarily unavailable.", 503);
            }
            return _response;
        }

        [Authorize]
        [HttpPost("CreateStripeSession")]
        public async Task<ActionResult<ResponseDto>> CreateStripeSession([FromBody] StripeRequestDto stripeRequestDto)
        {
            var currentUserId = getUserId();
            if (currentUserId == null) return Forbid();
            if (stripeRequestDto?.OrderHeader == null) return BadRequest();
            try
            {
                var orderHeader = await _db.OrderHeaders.Include(o => o.OrderDetails)
                    .FirstOrDefaultAsync(o => o.OrderHeaderId == stripeRequestDto.OrderHeader.OrderHeaderId);
                if (orderHeader == null) return NotFound();
                if (orderHeader.UserId != currentUserId) return Forbid();
                if (orderHeader.Status != SD.Status_Pending) return failure("Only pending orders can start checkout.", 409);
                if (!validCheckoutUrl(stripeRequestDto.ApprovedUrl) || !validCheckoutUrl(stripeRequestDto.CancelUrl))
                    return failure("Checkout URLs must be absolute HTTP or HTTPS URLs.", 400);
                if (!validOrderPricing(orderHeader) || orderHeader.OrderTotal <= 0)
                    return failure("This order cannot start a payment session.", 409);
                if (!string.IsNullOrWhiteSpace(orderHeader.StripeSessionId))
                {
                    var existing = await new SessionService().GetAsync(orderHeader.StripeSessionId);
                    if (existing.Id != orderHeader.StripeSessionId || existing.Status != "open"
                        || existing.PaymentStatus != "unpaid" || existing.Currency != "usd"
                        || existing.AmountTotal != cents(orderHeader.OrderTotal)
                        || existing.ClientReferenceId != orderHeader.OrderHeaderId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        || string.IsNullOrWhiteSpace(existing.Url))
                        return failure("An existing checkout must be resolved before starting another payment.", 409);
                    stripeRequestDto.OrderHeader = _mapper.Map<OrderHeaderDto>(orderHeader);
                    stripeRequestDto.StripeSessionId = existing.Id;
                    stripeRequestDto.StripeSessionUrl = existing.Url;
                    _response.Result = stripeRequestDto;
                    return _response;
                }
                var options = new SessionCreateOptions
                {
                    SuccessUrl = stripeRequestDto.ApprovedUrl, CancelUrl = stripeRequestDto.CancelUrl,
                    Mode = "payment", ClientReferenceId = orderHeader.OrderHeaderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    LineItems = orderHeader.OrderDetails.Select(item => new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = cents(item.Price), Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = item.ProductName }
                        }, Quantity = item.Count
                    }).ToList()
                };
                if (orderHeader.Discount > 0)
                {
                    var coupon = await new Stripe.CouponService().CreateAsync(new CouponCreateOptions
                    {
                        AmountOff = cents(orderHeader.Discount), Currency = "usd", Duration = "once",
                        Name = orderHeader.CouponCode
                    }, new RequestOptions { IdempotencyKey = $"order-{orderHeader.OrderHeaderId}-discount-v1" });
                    options.Discounts = new List<SessionDiscountOptions> { new() { Coupon = coupon.Id } };
                }
                var session = await new SessionService().CreateAsync(options,
                    new RequestOptions { IdempotencyKey = $"order-{orderHeader.OrderHeaderId}-checkout-v1" });
                if (string.IsNullOrWhiteSpace(session.Id) || string.IsNullOrWhiteSpace(session.Url))
                    return failure("Payment service returned an incomplete session.", 503);
                orderHeader.StripeSessionId = session.Id;
                await _db.SaveChangesAsync();
                stripeRequestDto.OrderHeader = _mapper.Map<OrderHeaderDto>(orderHeader);
                stripeRequestDto.StripeSessionId = session.Id;
                stripeRequestDto.StripeSessionUrl = session.Url;
                _response.Result = stripeRequestDto;
            }
            catch (Exception)
            {
                return failure("Checkout is temporarily unavailable.", 503);
            }
            return _response;
        }

        [Authorize]
        [HttpPost("ValidateStripeSession")]
        public async Task<ActionResult<ResponseDto>> ValidateStripeSession([FromBody] int orderHeaderId)
        {
            var currentUserId = getUserId();
            if (currentUserId == null) return Forbid();
            try
            {
                OrderHeader? orderHeader = await _db.OrderHeaders.FirstOrDefaultAsync(u => u.OrderHeaderId == orderHeaderId);
                if (orderHeader == null) return NotFound();
                if (orderHeader.UserId != currentUserId) return Forbid();

                if (orderHeader.Status == SD.Status_Approved || orderHeader.Status == SD.Status_ReadyForPickup
                    || orderHeader.Status == SD.Status_Completed)
                {
                    _response.Result = _mapper.Map<OrderHeaderDto>(orderHeader);
                    return _response;
                }
                if (orderHeader.Status != SD.Status_Pending || string.IsNullOrWhiteSpace(orderHeader.StripeSessionId))
                    return failure("This order cannot be confirmed.", 409);
                var session = await new SessionService().GetAsync(orderHeader.StripeSessionId);
                if (session.Id != orderHeader.StripeSessionId
                    || session.ClientReferenceId != orderHeader.OrderHeaderId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    || session.PaymentStatus != "paid" || session.Currency != "usd"
                    || session.AmountTotal != cents(orderHeader.OrderTotal) || string.IsNullOrWhiteSpace(session.PaymentIntentId))
                    return failure("Payment does not match the order.", 409);
                var paymentIntent = await new PaymentIntentService().GetAsync(session.PaymentIntentId);
                if (paymentIntent.Id != session.PaymentIntentId || paymentIntent.Status != "succeeded"
                    || paymentIntent.Currency != "usd" || paymentIntent.AmountReceived != cents(orderHeader.OrderTotal))
                    return failure("Payment has not completed for the order amount.", 409);
                orderHeader.PaymentIntentId = paymentIntent.Id;
                orderHeader.Status = SD.Status_Approved;
                await _db.SaveChangesAsync();
                var rewardsDto = new RewardsDto { OrderId = orderHeader.OrderHeaderId,
                    RewardsActivity = Convert.ToInt32(orderHeader.OrderTotal), UserId = orderHeader.UserId };
                await _messageBus.PublishMessage(rewardsDto,
                    _configuration.GetValue<string>("TopicAndQueueNames:OrderCreatedTopic"));
                _response.Result = _mapper.Map<OrderHeaderDto>(orderHeader);
            }
            catch (Exception)
            {
                // Payment approval may already be stored if reward publication fails. See SECURITY-SETUP.md.
                return failure("Payment confirmation could not finish. Check the order status before retrying.", 503);
            }
            return _response;
        }

        [Authorize(Roles = SD.RoleAdmin)]
        [HttpPost("UpdateOrderStatus/{orderId:int}")]
        public async Task<ActionResult<ResponseDto>> UpdateOrderStatus(int orderId, [FromBody] string newStatus)
        {
            if (getUserId() == null) return Forbid();
            if (newStatus != SD.Status_ReadyForPickup && newStatus != SD.Status_Completed && newStatus != SD.Status_Cancelled) return BadRequest();
            try
            {
                OrderHeader? orderHeader = await _db.OrderHeaders.FirstOrDefaultAsync(u => u.OrderHeaderId == orderId);
                if (orderHeader == null) return NotFound();
                if (orderHeader.Status == newStatus) return _response;
                var allowed = (orderHeader.Status == SD.Status_Approved && newStatus == SD.Status_ReadyForPickup)
                    || (orderHeader.Status == SD.Status_ReadyForPickup && newStatus == SD.Status_Completed)
                    || ((orderHeader.Status == SD.Status_Approved || orderHeader.Status == SD.Status_ReadyForPickup)
                        && newStatus == SD.Status_Cancelled);
                if (!allowed) return failure("The order status transition is not allowed.", 409);
                if (newStatus == SD.Status_Cancelled)
                {
                    if (string.IsNullOrWhiteSpace(orderHeader.PaymentIntentId))
                        return failure("A paid order requires its stored payment identifier before refunding.", 409);
                    var refund = await new RefundService().CreateAsync(new RefundCreateOptions
                    {
                        Reason = RefundReasons.RequestedByCustomer, PaymentIntent = orderHeader.PaymentIntentId
                    }, new RequestOptions { IdempotencyKey = $"order-{orderHeader.OrderHeaderId}-refund-v1" });
                    if (refund.Status != "succeeded" && refund.Status != "pending")
                        return failure("The refund was not accepted.", 409);
                }
                orderHeader.Status = newStatus;
                await _db.SaveChangesAsync();
            }
            catch (Exception)
            {
                return failure("The order status could not be updated.", 503);
            }
            return _response;
        }

        private static decimal money(double value)
        {
            if (!double.IsFinite(value) || value < 0) throw new InvalidOperationException("Invalid money value.");
            return decimal.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
        }

        private static long cents(double value) => checked((long)(money(value) * 100m));

        private static bool validCheckoutUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        private static bool validOrderPricing(OrderHeader order)
        {
            var items = order.OrderDetails?.ToList();
            if (items == null || items.Count == 0 || items.Any(i => i.Count <= 0)) return false;
            var subtotal = items.Sum(i => money(i.Price) * i.Count);
            var discount = money(order.Discount);
            return discount <= subtotal && money(order.OrderTotal) == subtotal - discount;
        }

        private ObjectResult failure(string message, int statusCode) =>
            StatusCode(statusCode, new ResponseDto { IsSuccess = false, Message = message });

        private string? getUserId()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }
    }
}
