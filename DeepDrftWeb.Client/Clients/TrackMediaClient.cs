using Microsoft.Extensions.DependencyInjection;

namespace DeepDrftWeb.Client.Clients;

public class TrackMediaClient
{
    private readonly HttpClient _http;
    
    public TrackMediaClient(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient("DeepDrft.Content");
    }

    public async Task<Stream> GetTrackMedia(string trackId)
    {
        return await _http.GetStreamAsync($"api/track/{trackId}");
    }
}