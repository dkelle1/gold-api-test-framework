using System.Net.Http.Headers;
using OrderService.Models;

namespace OrderService.Clients;

public interface IProductServiceClient
{
    Task<ProductDto?> GetProductAsync(int productId);
    void SetBearerToken(string token);
}

public class ProductServiceClient : IProductServiceClient
{
    private readonly HttpClient _httpClient;

    public ProductServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void SetBearerToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<ProductDto?> GetProductAsync(int productId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/products/{productId}");
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ProductDto>();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
