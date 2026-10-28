using Mango.Services.ProductAPI.Models;
using Mango.Services.ProductAPI.Models.Dto;

namespace Mango.Services.ProductAPI;

public sealed class MappingConfig
{
    public Product ToProduct(ProductDto source)
    {
        if (source is null) return null!;
        return new Product
        {
            ProductId = source.ProductId,
            Name = source.Name,
            Price = source.Price,
            Description = source.Description,
            CategoryName = source.CategoryName,
            ImageUrl = source.ImageUrl,
            ImageLocalPath = source.ImageLocalPath,
        };
    }

    public ProductDto ToProductDto(Product source)
    {
        if (source is null) return null!;
        return new ProductDto
        {
            ProductId = source.ProductId,
            Name = source.Name,
            Price = source.Price,
            Description = source.Description,
            CategoryName = source.CategoryName,
            ImageUrl = source.ImageUrl,
            ImageLocalPath = source.ImageLocalPath,
        };
    }

    public List<Product> ToProductList(IEnumerable<ProductDto> source) =>
        source?.Select(ToProduct).ToList() ?? new List<Product>();

    public List<ProductDto> ToProductDtoList(IEnumerable<Product> source) =>
        source?.Select(ToProductDto).ToList() ?? new List<ProductDto>();
}
