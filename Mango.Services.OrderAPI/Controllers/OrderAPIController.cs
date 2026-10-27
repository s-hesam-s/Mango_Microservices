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
        private readonly IMessageBus _messageBus;
        private readonly IConfiguration _configuration;
        public OrderAPIController(AppDbContext db, IProductService productService, IMapper mapper, 
                                  IConfiguration configuration, IMessageBus messageBus)
        {
            _db = db;
            this._response = new ResponseDto();
            _productService = productService;
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
                OrderHeaderDto orderHeaderDto = _mapper.Map<OrderHeaderDto>(cartDto.CartHeader);
                orderHeaderDto.OrderTime = DateTime.Now;
                orderHeaderDto.Status = SD.Status_Pending;
                orderHeaderDto.OrderDetails = _mapper.Map<IEnumerable<OrderDetailsDto>>(cartDto.CartDetails);
                orderHeaderDto.OrderTotal = Math.Round(orderHeaderDto.OrderTotal, 2);

                OrderHeader orderCreated = _db.OrderHeaders.Add(_mapper.Map<OrderHeader>(orderHeaderDto)).Entity;
                await _db.SaveChangesAsync();

                orderHeaderDto.OrderHeaderId = orderCreated.OrderHeaderId;
                _response.Result = orderHeaderDto;
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.Message;
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
                OrderHeader? orderHeader = await _db.OrderHeaders.FirstOrDefaultAsync(u => u.OrderHeaderId == stripeRequestDto.OrderHeader.OrderHeaderId);
                if (orderHeader == null) return NotFound();
                if (orderHeader.UserId != currentUserId) return Forbid();
                stripeRequestDto.OrderHeader.UserId = currentUserId;
                var options = new SessionCreateOptions
                {
                    SuccessUrl = stripeRequestDto.ApprovedUrl,
                    CancelUrl = stripeRequestDto.CancelUrl,
                    LineItems = new List<SessionLineItemOptions>(),
                    Mode = "payment",
                };

                var DiscountsObj = new List<SessionDiscountOptions>()
                {
                    new SessionDiscountOptions
                    {
                        Coupon = stripeRequestDto.OrderHeader.CouponCode
                    }
                };

                foreach (var item in stripeRequestDto.OrderHeader.OrderDetails)
                {
                    var sessionLineItem = new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(item.Price * 100), // $20.99 -> 2099
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.Product.Name
                            }
                        },
                        Quantity = item.Count
                    };

                    options.LineItems.Add(sessionLineItem);
                }

                if (stripeRequestDto.OrderHeader.Discount > 0)
                {
                    options.Discounts = DiscountsObj;
                }

                var service = new SessionService();
                Session session = service.Create(options);
                stripeRequestDto.StripeSessionUrl = session.Url;
                orderHeader.StripeSessionId = session.Id;
                _db.SaveChanges();
                _response.Result = stripeRequestDto;

            }
            catch (Exception ex)
            {
                _response.Message = ex.Message;
                _response.IsSuccess = false;
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

                var service = new SessionService();
                Session session = service.Get(orderHeader.StripeSessionId);

                var paymentIntentService = new PaymentIntentService();
                PaymentIntent paymentIntent = paymentIntentService.Get(session.PaymentIntentId);

                if (paymentIntent.Status == "succeeded")
                {
                    //then payment was successful
                    orderHeader.PaymentIntentId = paymentIntent.Id;
                    orderHeader.Status = SD.Status_Approved;
                    _db.SaveChanges();

                    RewardsDto rewardsDto = new()
                    {
                        OrderId = orderHeader.OrderHeaderId,
                        RewardsActivity = Convert.ToInt32(orderHeader.OrderTotal),
                        UserId = orderHeader.UserId
                    };
                    string topicName = _configuration.GetValue<string>("TopicAndQueueNames:OrderCreatedTopic");
                    await _messageBus.PublishMessage(rewardsDto, topicName);

                    _response.Result = _mapper.Map<OrderHeaderDto>(orderHeader);
                }
            }
            catch (Exception ex)
            {
                _response.Message = ex.Message;
                _response.IsSuccess = false;
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
                if (orderHeader != null)
                {
                    if (newStatus == SD.Status_Cancelled)
                    {
                        //we will give refund
                        var options = new RefundCreateOptions
                        {
                            Reason = RefundReasons.RequestedByCustomer,
                            PaymentIntent = orderHeader.PaymentIntentId
                        };

                        var service = new RefundService();
                        Refund refund = service.Create(options);
                    }
                    orderHeader.Status = newStatus;
                    _db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
            }
            return _response;
        }

        private string? getUserId()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }
    }
}
