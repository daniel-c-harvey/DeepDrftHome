using Microsoft.Extensions.DependencyInjection;

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

    public async Task<TrackMediaResponse> GetTrackMedia(string trackId)
    {
        var response = await _http.GetAsync($"track/{trackId}");
        response.EnsureSuccessStatusCode();
        
        var contentLength = response.Content.Headers.ContentLength ?? 0;
        var stream = await response.Content.ReadAsStreamAsync();
        
        return new TrackMediaResponse(stream, contentLength);
    }
}