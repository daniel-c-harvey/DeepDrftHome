using Microsoft.AspNetCore.Mvc;

namespace DeepDrftPublic.Controllers;

/// <summary>
/// Proxies public track API calls to DeepDrftAPI so the browser never makes
/// cross-origin requests. The WASM client points both named HttpClients at
/// this host; this controller forwards unauthenticated public routes upstream.
/// SSR prerender calls DeepDrftAPI directly (server-to-server) via the same
/// named clients — no proxy hop needed on the server side.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TrackController : ControllerBase
{
    private readonly HttpClient _upstream;
    private readonly ILogger<TrackController> _logger;

    public TrackController(IHttpClientFactory httpClientFactory, ILogger<TrackController> logger)
    {
        _upstream = httpClientFactory.CreateClient("DeepDrft.API");
        _logger = logger;
    }

    /// <summary>Proxies paged track metadata from DeepDrftAPI.</summary>
    [HttpGet("page")]
    public async Task<ActionResult> GetPage(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortColumn = null,
        [FromQuery] bool sortDescending = false,
        CancellationToken ct = default)
    {
        var query = $"api/track/page?page={page}&pageSize={pageSize}&sortDescending={sortDescending}";
        if (!string.IsNullOrWhiteSpace(sortColumn))
            query += $"&sortColumn={Uri.EscapeDataString(sortColumn)}";

        HttpResponseMessage upstream;
        try
        {
            upstream = await _upstream.GetAsync(query, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstream call to DeepDrftAPI track/page failed");
            return StatusCode(502, "Upstream unavailable");
        }

        using (upstream)
        {
            if (!upstream.IsSuccessStatusCode)
            {
                _logger.LogWarning("DeepDrftAPI track/page returned {Status}", (int)upstream.StatusCode);
                return StatusCode((int)upstream.StatusCode);
            }

            var json = await upstream.Content.ReadAsStringAsync(ct);
            return Content(json, "application/json");
        }
    }

    /// <summary>
    /// Proxies audio streaming from DeepDrftAPI. Passes the optional byte offset
    /// so seek-beyond-buffer works through the proxy without buffering.
    /// </summary>
    [HttpGet("{trackId}")]
    public async Task<ActionResult> GetTrack(
        string trackId,
        [FromQuery] long offset = 0,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Proxying track {TrackId} offset {Offset}", trackId, offset);

        var path = offset == 0
            ? $"api/track/{Uri.EscapeDataString(trackId)}"
            : $"api/track/{Uri.EscapeDataString(trackId)}?offset={offset}";

        HttpResponseMessage upstream;
        try
        {
            upstream = await _upstream.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstream call to DeepDrftAPI track/{TrackId} failed", trackId);
            return StatusCode(502, "Upstream unavailable");
        }

        if (!upstream.IsSuccessStatusCode)
        {
            upstream.Dispose();
            _logger.LogWarning("DeepDrftAPI track/{TrackId} returned {Status}", trackId, (int)upstream.StatusCode);
            return StatusCode((int)upstream.StatusCode);
        }

        // Do NOT dispose upstream here — File() takes ownership of the response stream
        // and disposes it after the body is sent.
        var contentType = upstream.Content.Headers.ContentType?.ToString() ?? "audio/wav";
        var contentLength = upstream.Content.Headers.ContentLength;

        // Forward Content-Length so the WASM player has duration info from the WAV header length.
        if (contentLength.HasValue)
            Response.ContentLength = contentLength.Value;

        var stream = await upstream.Content.ReadAsStreamAsync(ct);
        return File(stream, contentType, enableRangeProcessing: false);
    }
}
