using DeepDrftModels.Entities;
using DeepDrftModels.Models;
using NetBlocks.Models;
using System.Text.Json;
using System.Web;

namespace DeepDrftWeb.Client.Clients;

public class TrackClient : ApiClient<ClientConfig>
{
    public TrackClient(ClientConfig config) : base(config) { }

    public async Task<ApiResult<PagedResult<TrackEntity>>> GetPage(
        int pageNumber, 
        int pageSize, 
        string? sortColumn = null, 
        bool sortDescending = false)
    {
        var uriBuilder = new UriBuilder(http.BaseAddress!)
        {
            Path = "api/track/page"
        };

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["pageNumber"] = pageNumber.ToString();
        query["pageSize"] = pageSize.ToString();
        
        if (!string.IsNullOrEmpty(sortColumn))
            query["sortColumn"] = sortColumn;
        
        if (sortDescending)
            query["sortDescending"] = "true";

        uriBuilder.Query = query.ToString();

        var response = await http.GetAsync(uriBuilder.Uri);
        var json = await response.Content.ReadAsStringAsync();
        
        var dto = JsonSerializer.Deserialize<ApiResultDto<PagedResult<TrackEntity>>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return dto?.From() ?? ApiResult<PagedResult<TrackEntity>>.CreateFailResult("Failed to deserialize response");
    }
}