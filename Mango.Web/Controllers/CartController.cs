using Mango.Web.Models;
using Mango.Web.Service;
using Mango.Web.Service.IService;
using Mango.Web.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;

namespace Mango.Web.Controllers
{
    [Authorize]
    public class CartController : Controller
    {
        private readonly ICartService _cartService;
        private readonly IOrderService _orderService;

        public CartController(ICartService cartService, IOrderService orderService)
        {
            _cartService = cartService;
            _orderService = orderService;
        }

        [Authorize]
        public async Task<IActionResult> CartIndex()
        {
            return View(await loadCartDtoBasedOnLoggedInUser());
        }

        [Authorize]
        public async Task<IActionResult> Checkout()
        {
            return View(await loadCartDtoBasedOnLoggedInUser());
        }

        [HttpPost]
        [ActionName("Checkout")]
        public async Task<IActionResult> Checkout(CartDto cartDto)
        {

            CartDto cart = await loadCartDtoBasedOnLoggedInUser();
            if (cartDto?.CartHeader == null || cart.CartDetails?.Any() != true || !ModelState.IsValid)
            {
                ModelState.AddModelError("", "Check your contact details and cart before checkout.");
                return View(cart);
            }
            cart.CartHeader.Phone = cartDto.CartHeader.Phone;
            cart.CartHeader.Email = cartDto.CartHeader.Email;
            cart.CartHeader.Name = cartDto.CartHeader.Name;
            try
            {
                var response = await _orderService.CreateOrder(cart);
                var order = readResult<OrderHeaderDto>(response);
                if (order == null || order.OrderHeaderId <= 0)
                {
                    ModelState.AddModelError("", "The order could not be created. Please try again later.");
                    return View(cart);
                }
                var domain = Request.Scheme + "://" + Request.Host.Value + "/";
                var stripeResponse = await _orderService.CreateStripeSession(new StripeRequestDto
                {
                    ApprovedUrl = domain + "cart/Confirmation?orderId=" + order.OrderHeaderId,
                    CancelUrl = domain + "cart/checkout", OrderHeader = order
                });
                var session = readResult<StripeRequestDto>(stripeResponse);
                if (session == null || !Uri.TryCreate(session.StripeSessionUrl, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps)
                {
                    ModelState.AddModelError("", "Payment could not be started. Check your orders before retrying.");
                    return View(cart);
                }
                Response.Headers["Location"] = session.StripeSessionUrl;
                return new StatusCodeResult(303);
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "Checkout is temporarily unavailable. Check your orders before retrying.");
                return View(cart);
            }
        }

        public async Task<IActionResult> Confirmation(int orderId)
        {
            try
            {
                var response = await _orderService.ValidateStripeSession(orderId);
                var order = readResult<OrderHeaderDto>(response);
                if (order?.OrderHeaderId == orderId && (order.Status == SD.Status_Approved
                    || order.Status == SD.Status_ReadyForPickup || order.Status == SD.Status_Completed))
                    return View(orderId);
            }
            catch (Exception) { }
            ViewData["PaymentError"] = "Payment could not be confirmed. Check your order status before retrying.";
            return View("Confirmation", orderId);
        }

        public async Task<IActionResult> Remove(int cartDetailsId)
        {
            var userId = User.Claims.Where(u => u.Type == JwtRegisteredClaimNames.Sub)?.FirstOrDefault()?.Value;
            ResponseDto? response = await _cartService.RemoveFromCartAsync(cartDetailsId);
            if (response != null && response.IsSuccess)
            {
                TempData["success"] = "Cart updated successfully";
                return RedirectToAction(nameof(CartIndex));
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ApplyCoupon(CartDto cartDto)
        {

            ResponseDto? response = await _cartService.ApplyCouponAsync(cartDto);
            if (response != null && response.IsSuccess)
            {
                TempData["success"] = "Cart updated successfully";
                return RedirectToAction(nameof(CartIndex));
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> RemoveCoupon(CartDto cartDto)
        {
            cartDto.CartHeader.CouponCode = "";
            ResponseDto? response = await _cartService.ApplyCouponAsync(cartDto);
            if (response != null && response.IsSuccess)
            {
                TempData["success"] = "Cart updated successfully";
                return RedirectToAction(nameof(CartIndex));
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> EmailCart(CartDto cartDto)
        {
            CartDto cart = await loadCartDtoBasedOnLoggedInUser();
            cart.CartHeader.Email = User.Claims.Where(u => u.Type == JwtRegisteredClaimNames.Email)?.FirstOrDefault()?.Value;
            ResponseDto? response = await _cartService.EmailCart(cart); 
            if (response != null && response.IsSuccess)
            {
                TempData["success"] = "Email will be processed and sent shortly.";
                return RedirectToAction(nameof(CartIndex));
            }
            return View();
        }

        private async Task<CartDto> loadCartDtoBasedOnLoggedInUser()
        {
            var userId = User.Claims.FirstOrDefault(u => u.Type == JwtRegisteredClaimNames.Sub
                || u.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            try
            {
                var response = await _cartService.GetCartByUserIdAsnyc(userId);
                var cart = readResult<CartDto>(response);
                if (cart?.CartHeader != null && cart.CartDetails != null) return cart;
            }
            catch (Exception) { }
            ModelState.AddModelError("", "The cart is currently unavailable.");
            return new CartDto { CartHeader = new CartHeaderDto(), CartDetails = Array.Empty<CartDetailsDto>() };
        }

        private static T? readResult<T>(ResponseDto? response) where T : class
        {
            if (response?.IsSuccess != true || response.Result == null) return null;
            if (response.Result is T typed) return typed;
            try { return JsonConvert.DeserializeObject<T>(Convert.ToString(response.Result)!); }
            catch (JsonException) { return null; }
        }
    }
}
