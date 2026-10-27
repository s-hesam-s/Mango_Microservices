using AutoMapper;
using Mango.MessageBus;
using Mango.Services.ShoppingCartAPI.Data;
using Mango.Services.ShoppingCartAPI.Models;
using Mango.Services.ShoppingCartAPI.Models.Dto;
using Mango.Services.ShoppingCartAPI.Service.IService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Mango.Services.ShoppingCartAPI.Controllers
{
    [Route("api/cart")]
    [ApiController]
    [Authorize]
    public class CartAPIController : ControllerBase
    {
        private ResponseDto _response;
        private IMapper _mapper;
        private readonly AppDbContext _db;
        private IProductService _productService;
        private ICouponService _couponService;
        private IConfiguration _configuration;
        private readonly IMessageBus _messageBus;
        public CartAPIController(AppDbContext db,
            IMapper mapper, IProductService productService, ICouponService couponService, IMessageBus messageBus, IConfiguration configuration)
        {
            _db = db;
            _productService = productService;
            this._response = new ResponseDto();
            _mapper = mapper;
            _couponService = couponService;
            _messageBus = messageBus;
            _configuration = configuration;
        }
        [HttpGet("GetCart/{userId}")]
        public async Task<ActionResult<ResponseDto>> GetCart(string userId)
        {
            var currentUserId = getUserId();
            if (currentUserId == null || userId != currentUserId) return Forbid();
            try
            {
                var header = await _db.CartHeaders.FirstOrDefaultAsync(u => u.UserId == currentUserId);
                if (header == null) return NotFound();
                CartDto cart = new()
                {
                    CartHeader = _mapper.Map<CartHeaderDto>(header)
                };
                cart.CartDetails = _mapper.Map<IEnumerable<CartDetailsDto>>(_db.CartDetails
                    .Where(u => u.CartHeaderId == cart.CartHeader.CartHeaderId));

                IEnumerable<ProductDto> productDtos = await _productService.GetProducts();

                foreach (var item in cart.CartDetails)
                {
                    item.Product = productDtos.FirstOrDefault(u => u.ProductId == item.ProductId);
                    cart.CartHeader.CartTotal += (item.Count * item.Product.Price);
                }

                //apply coupon if any
                if (!string.IsNullOrEmpty(cart.CartHeader.CouponCode))
                {
                    CouponDto coupon = await _couponService.GetCoupon(cart.CartHeader.CouponCode);
                    if (coupon != null && cart.CartHeader.CartTotal > coupon.MinAmount)
                    {
                        cart.CartHeader.CartTotal -= coupon.DiscountAmount;
                        cart.CartHeader.Discount = coupon.DiscountAmount;
                    }
                }

                _response.Result = cart;
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.Message;
            }
            return _response;
        }

        [HttpPost("ApplyCoupon")]
        public async Task<ActionResult<ResponseDto>> ApplyCoupon([FromBody] CartDto cartDto)
        {
            var denial = bindCartOwner(cartDto);
            if (denial != null) return denial;
            try
            {
                var cartFromDb = await _db.CartHeaders.FirstOrDefaultAsync(u => u.UserId == cartDto.CartHeader.UserId);
                if (cartFromDb == null) return NotFound();
                if (cartDto.CartHeader.CartHeaderId != 0 && cartDto.CartHeader.CartHeaderId != cartFromDb.CartHeaderId) return Forbid();
                cartFromDb.CouponCode = cartDto.CartHeader.CouponCode;
                _db.CartHeaders.Update(cartFromDb);
                await _db.SaveChangesAsync();
                _response.Result = true;
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.ToString();
            }
            return _response;
        }

        [HttpPost("CartUpsert")]
        public async Task<ActionResult<ResponseDto>> CartUpsert(CartDto cartDto)
        {
            var denial = bindCartOwner(cartDto);
            if (denial != null) return denial;
            var item = cartDto.CartDetails?.FirstOrDefault();
            if (item == null) return BadRequest();
            item.CartHeader = null;
            try
            {
                var cartHeaderFromDb = await _db.CartHeaders.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == cartDto.CartHeader.UserId);
                if (cartHeaderFromDb == null)
                {
                    //create header and details
                    CartHeader cartHeader = _mapper.Map<CartHeader>(cartDto.CartHeader);
                    cartHeader.CartHeaderId = 0;
                    _db.CartHeaders.Add(cartHeader);
                    await _db.SaveChangesAsync();
                    cartDto.CartHeader.CartHeaderId = cartHeader.CartHeaderId;
                    item.CartHeaderId = cartHeader.CartHeaderId;
                    item.CartDetailsId = 0;
                    _db.CartDetails.Add(_mapper.Map<CartDetails>(item));
                    await _db.SaveChangesAsync();
                }
                else
                {
                    cartDto.CartHeader.CartHeaderId = cartHeaderFromDb.CartHeaderId;
                    //if header is not null
                    //check if details has same product
                    var cartDetailsFromDb = await _db.CartDetails.AsNoTracking().FirstOrDefaultAsync(
                        u => u.ProductId == cartDto.CartDetails.First().ProductId &&
                        u.CartHeaderId == cartHeaderFromDb.CartHeaderId);
                    if (cartDetailsFromDb == null)
                    {
                        //create cartdetails
                        cartDto.CartDetails.First().CartHeaderId = cartHeaderFromDb.CartHeaderId;
                        cartDto.CartDetails.First().CartDetailsId = 0;
                        _db.CartDetails.Add(_mapper.Map<CartDetails>(cartDto.CartDetails.First()));
                        await _db.SaveChangesAsync();
                    }
                    else
                    {
                        //update count in cart details
                        cartDto.CartDetails.First().Count += cartDetailsFromDb.Count;
                        cartDto.CartDetails.First().CartHeaderId = cartDetailsFromDb.CartHeaderId;
                        cartDto.CartDetails.First().CartDetailsId = cartDetailsFromDb.CartDetailsId;
                        _db.CartDetails.Update(_mapper.Map<CartDetails>(cartDto.CartDetails.First()));
                        await _db.SaveChangesAsync();
                    }
                }
                _response.Result = cartDto;
            }
            catch (Exception ex)
            {
                _response.Message = ex.Message.ToString();
                _response.IsSuccess = false;
            }
            return _response;
        }

        [HttpPost("RemoveCart")]
        public async Task<ActionResult<ResponseDto>> RemoveCart([FromBody] int cartDetailsId)
        {
            var currentUserId = getUserId();
            if (currentUserId == null) return Forbid();
            try
            {
                CartDetails? cartDetails = await _db.CartDetails.Include(u => u.CartHeader)
                   .FirstOrDefaultAsync(u => u.CartDetailsId == cartDetailsId);
                if (cartDetails == null) return NotFound();
                if (cartDetails.CartHeader?.UserId != currentUserId) return Forbid();

                int totalCountofCartItem = _db.CartDetails.Where(u => u.CartHeaderId == cartDetails.CartHeaderId).Count();
                _db.CartDetails.Remove(cartDetails);
                if (totalCountofCartItem == 1)
                {
                    var cartHeaderToRemove = await _db.CartHeaders
                       .FirstOrDefaultAsync(u => u.CartHeaderId == cartDetails.CartHeaderId);

                    _db.CartHeaders.Remove(cartHeaderToRemove);
                }
                await _db.SaveChangesAsync();

                _response.Result = true;
            }
            catch (Exception ex)
            {
                _response.Message = ex.Message.ToString();
                _response.IsSuccess = false;
            }
            return _response;
        }

        [HttpPost("EmailCartRequest")]
        public async Task<ActionResult<ResponseDto>> EmailCartRequest([FromBody] CartDto cartDto)
        {
            var denial = bindCartOwner(cartDto);
            if (denial != null) return denial;
            try
            {
                var header = await _db.CartHeaders.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == cartDto.CartHeader.UserId);
                if (header == null) return NotFound();
                if (cartDto.CartHeader.CartHeaderId != 0 && cartDto.CartHeader.CartHeaderId != header.CartHeaderId) return Forbid();
                cartDto.CartHeader.CartHeaderId = header.CartHeaderId;
                await _messageBus.PublishMessage(cartDto, _configuration.GetValue<string>("TopicAndQueueNames:EmailShoppingCartQueue"));
                _response.Result = true;
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.ToString();
            }
            return _response;
        }

        private string? getUserId()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }

        private ActionResult? bindCartOwner(CartDto cartDto)
        {
            var userId = getUserId();
            if (userId == null) return Forbid();
            if (cartDto?.CartHeader == null) return BadRequest();
            if (!string.IsNullOrEmpty(cartDto.CartHeader.UserId) && cartDto.CartHeader.UserId != userId) return Forbid();
            cartDto.CartHeader.UserId = userId;
            return null;
        }
    }
}
