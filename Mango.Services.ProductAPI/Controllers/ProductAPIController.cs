using AutoMapper;
using Mango.Services.ProductAPI.Security;
using Mango.Services.ProductAPI.Data;
using Mango.Services.ProductAPI.Models;
using Mango.Services.ProductAPI.Models.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mango.Services.ProductAPI.Controllers
{
    [Route("api/product")]
    [ApiController]
    //[Authorize]
    public class ProductAPIController : ControllerBase
    {
        private readonly AppDbContext _db;
        private ResponseDto _response;
        private IMapper _mapper;

        public ProductAPIController(AppDbContext db, IMapper mapper)
        {
            _db = db;
            _mapper = mapper;
            _response = new ResponseDto();
        }

        [HttpGet]
        public ResponseDto Get()
        {
            try
            {
                IEnumerable<Product> objList = _db.Products.ToList();
                _response.Result = _mapper.Map<IEnumerable<ProductDto>>(objList);
            }
            catch (Exception)
            {
                _response.IsSuccess = false;
                _response.Message = "The operation could not be completed.";
            }
            return _response;
        }

        [HttpGet]
        [Route("{id:int}")]
        public ResponseDto Get(int id)
        {
            try
            {
                Product obj = _db.Products.First(u => u.ProductId == id);
                _response.Result = _mapper.Map<ProductDto>(obj);
            }
            catch (Exception)
            {
                _response.IsSuccess = false;
                _response.Message = "The operation could not be completed.";
            }
            return _response;
        }

        [HttpPost]
        [Authorize(Roles = "ADMIN")]
        public ResponseDto Post([FromForm] ProductDto ProductDto)
        {
            try
            {
                Product product = _mapper.Map<Product>(ProductDto);
                product.ProductId = 0;
                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var imagePath = ProductDto.Image == null ? null : ProductImageStorage.Save(ProductDto.Image, webRoot);
                product.ImageLocalPath = imagePath == null ? null : "wwwroot" + imagePath;
                product.ImageUrl = imagePath == null ? "https://placehold.co/600x400" :
                    $"{Request.Scheme}://{Request.Host}{Request.PathBase}" + imagePath;
                try { _db.Products.Add(product); _db.SaveChanges(); }
                catch
                {
                    if (imagePath != null) ProductImageStorage.DeleteManaged(product.ImageLocalPath, webRoot);
                    throw;
                }

                _response.Result = _mapper.Map<ProductDto>(product);
            }
            catch (Exception)
            {
                _response.IsSuccess = false;
                _response.Message = "The operation could not be completed.";
            }
            return _response;
        }


        [HttpPut]
        [Authorize(Roles = "ADMIN")]
        public ResponseDto Put([FromForm] ProductDto ProductDto)
        {
            try
            {
                var stored = _db.Products.AsNoTracking().FirstOrDefault(p => p.ProductId == ProductDto.ProductId);
                if (stored == null) { _response.IsSuccess = false; _response.Message = "Product not found."; return _response; }
                Product product = _mapper.Map<Product>(ProductDto);
                product.ImageLocalPath = stored.ImageLocalPath;
                product.ImageUrl = stored.ImageUrl;
                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                if (ProductDto.Image != null)
                {
                    var imagePath = ProductImageStorage.Save(ProductDto.Image, webRoot);
                    product.ImageLocalPath = "wwwroot" + imagePath;
                    product.ImageUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}" + imagePath;
                }
                try { _db.Products.Update(product); _db.SaveChanges(); }
                catch
                {
                    if (ProductDto.Image != null) ProductImageStorage.DeleteManaged(product.ImageLocalPath, webRoot);
                    throw;
                }
                if (ProductDto.Image != null)
                {
                    try { ProductImageStorage.DeleteManaged(stored.ImageLocalPath, webRoot); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* The saved product is retained if old-image cleanup fails. */ }
                }

                _response.Result = _mapper.Map<ProductDto>(product);
            }
            catch (Exception)
            {
                _response.IsSuccess = false;
                _response.Message = "The operation could not be completed.";
            }
            return _response;
        }

        [HttpDelete]
        [Route("{id:int}")]
        [Authorize(Roles = "ADMIN")]
        public ResponseDto Delete(int id)
        {
            try
            {
                Product obj = _db.Products.First(u => u.ProductId == id);
                _db.Products.Remove(obj);
                _db.SaveChanges();
                try { ProductImageStorage.DeleteManaged(obj.ImageLocalPath, Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* A committed deletion can leave an orphan image. */ }
            }
            catch (Exception)
            {
                _response.IsSuccess = false;
                _response.Message = "The operation could not be completed.";
            }
            return _response;
        }
    }
}