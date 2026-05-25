using DeepDrftModels.DTOs;
using Models.Common;
using NetBlocks.Models;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Http;

namespace DeepDrftPublic.Client.Clients;

public class TrackClient
{
    private readonly HttpClient _http;

    public TrackClient(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient("DeepDrft.API");
    }

    public async Task<ApiResult<PagedResult<TrackDto>>> GetPage(
        int pageNumber,
        int pageSize,
        string? sortColumn = null,
        bool sortDescending = false)
    {
        var queryArgs = new Dictionary<string, string?>(){
            ["page"] = pageNumber.ToString(),
            ["pageSize"] = pageSize.ToString()
        };

        if (!string.IsNullOrEmpty(sortColumn))
            queryArgs["sortColumn"] = sortColumn;

        if (sortDescending)
            queryArgs["sortDescending"] = "true";

        string query = QueryString.Create(queryArgs).ToString();

        var response = await _http.GetAsync($"api/track/page{query}");

        if (!response.IsSuccessStatusCode)
            return ApiResult<PagedResult<TrackDto>>.CreateFailResult($"HTTP {(int)response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync();
        var paged = JsonSerializer.Deserialize<PagedResult<TrackDto>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return paged is not null
            ? ApiResult<PagedResult<TrackDto>>.CreatePassResult(paged)
            : ApiResult<PagedResult<TrackDto>>.CreateFailResult("Failed to deserialize response");
    }
}