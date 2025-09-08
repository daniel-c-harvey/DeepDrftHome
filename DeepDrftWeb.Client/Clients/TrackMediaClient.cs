using Microsoft.Extensions.DependencyInjection;
using NetBlocks.Models;

namespace DeepDrftWeb.Client.Clients;

public class TrackMediaResponse
{
    public Stream Stream { get; }
    public long ContentLength { get; }
    
    public TrackMediaResponse(Stream stream, long contentLength)
    {
        Stream = stream;
        ContentLength = contentLength;
    }
}

public class TrackMediaClient
{
    private readonly HttpClient _http;
    
    public TrackMediaClient(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient("DeepDrft.Content");
    }

    public async Task<ApiResult<TrackMediaResponse>> GetTrackMedia(string trackId)
    {
        try
        {
            var response = await _http.GetAsync($"api/track/{trackId}");
            response.EnsureSuccessStatusCode();
        
            var contentLength = response.Content.Headers.ContentLength ?? 0;
            var stream = await response.Content.ReadAsStreamAsync();
        
            return ApiResult<TrackMediaResponse>.CreatePassResult(new TrackMediaResponse(stream, contentLength));
        }
        catch (Exception e)
        {
            return ApiResult<TrackMediaResponse>.CreateFailResult(e.Message);
        }
    }
}