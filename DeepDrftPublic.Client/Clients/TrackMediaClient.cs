using Microsoft.Extensions.DependencyInjection;
using NetBlocks.Models;

namespace DeepDrftPublic.Client.Clients;

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

    /// <summary>
    /// Fetches the WAV stream for a track, optionally starting from a byte offset.
    /// The cancellation token is forwarded to <see cref="HttpClient.GetAsync"/> so a
    /// navigation or seek-replacement aborts the in-flight server connection rather
    /// than leaving the server draining bytes into a dead socket.
    /// </summary>
    public async Task<ApiResult<TrackMediaResponse>> GetTrackMedia(
        string trackId,
        long byteOffset = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Build URL with optional offset parameter
            var url = byteOffset > 0
                ? $"api/track/{trackId}?offset={byteOffset}"
                : $"api/track/{trackId}";

            // Use HttpCompletionOption.ResponseHeadersRead to get stream immediately
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength ?? 0;
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            return ApiResult<TrackMediaResponse>.CreatePassResult(new TrackMediaResponse(stream, contentLength));
        }
        catch (Exception e)
        {
            return ApiResult<TrackMediaResponse>.CreateFailResult(e.Message);
        }
    }
}
