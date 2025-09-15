using Microsoft.Extensions.DependencyInjection;
using NetBlocks.Models;

namespace DeepDrftWeb.Client.Clients;

public class TrackMediaResponse : IDisposable
{
    public Stream Stream { get; }
    public long ContentLength { get; }
    
    public TrackMediaResponse(Stream stream, long contentLength)
    {
        Stream = stream;
        ContentLength = contentLength;
    }

    public void Dispose()
    {
        Stream?.Dispose();
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
            // Use HttpCompletionOption.ResponseHeadersRead to get stream immediately
            var response = await _http.GetAsync($"api/track/{trackId}", HttpCompletionOption.ResponseHeadersRead);
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