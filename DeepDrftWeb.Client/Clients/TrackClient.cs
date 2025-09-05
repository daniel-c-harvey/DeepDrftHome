using DeepDrftModels.Entities;
using DeepDrftModels.Models;
using NetBlocks.Models;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DeepDrftWeb.Client.Clients;

public class TrackClient
{
    private readonly HttpClient _http;

    public TrackClient(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient("DeepDrft.API");
    }

    public async Task<ApiResult<PagedResult<TrackEntity>>> GetPage(
        int pageNumber, 
        int pageSize, 
        string? sortColumn = null, 
        bool sortDescending = false)
    {
        var queryArgs = new Dictionary<string, string?>(){
            ["pageNumber"] = pageNumber.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        
        if (!string.IsNullOrEmpty(sortColumn))
            queryArgs["sortColumn"] = sortColumn;
        
        if (sortDescending)
            queryArgs["sortDescending"] = "true";

        string query = QueryString.Create(queryArgs).ToString();
        
        var response = await _http.GetAsync($"api/track/page{query}");
        var json = await response.Content.ReadAsStringAsync();
        
        var dto = JsonSerializer.Deserialize<ApiResultDto<PagedResult<TrackEntity>>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return dto?.From() ?? ApiResult<PagedResult<TrackEntity>>.CreateFailResult("Failed to deserialize response");
    }
}