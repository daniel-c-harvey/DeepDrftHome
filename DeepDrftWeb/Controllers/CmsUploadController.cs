using System.Net.Http.Headers;
using System.Security.Claims;
using DeepDrftModels.Entities;
using DeepDrftWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeepDrftWeb.Controllers;

/// <summary>
/// CMS upload surface. Proxies a WAV + metadata multipart form to DeepDrftContent's
/// POST api/track/upload, then persists the returned unpersisted TrackEntity to SQL via
/// ITrackService.Create. DeepDrftWeb intentionally does not reference DeepDrftContent.Services
/// (CMS-PLAN §5, Option B) — all vault access is over HTTP.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/cms")]
public class CmsUploadController : ControllerBase
{
    private const string ContentClientName = "DeepDrft.Content";
    private const string UploadPath = "api/track/upload";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITrackService _trackService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CmsUploadController> _logger;

    public CmsUploadController(
        IHttpClientFactory httpClientFactory,
        ITrackService trackService,
        IConfiguration configuration,
        ILogger<CmsUploadController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _trackService = trackService;
        _configuration = configuration;
        _logger = logger;
    }

    // Match DeepDrftContent's per-request ceiling so the proxy itself does not reject
    // a payload the downstream endpoint would accept.
    [HttpPost("track")]
    [RequestSizeLimit(1_073_741_824)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_073_741_824)]
    public async Task<ActionResult<TrackEntity>> UploadTrack(
        [FromForm] IFormFile? wav,
        [FromForm] string? trackName,
        [FromForm] string? artist,
        [FromForm] string? album,
        [FromForm] string? genre,
        [FromForm] string? releaseDate,
        CancellationToken cancellationToken)
    {
        if (wav is null || wav.Length == 0)
        {
            return BadRequest("WAV file is required");
        }

        if (string.IsNullOrWhiteSpace(trackName))
        {
            return BadRequest("trackName is required");
        }

        if (string.IsNullOrWhiteSpace(artist))
        {
            return BadRequest("artist is required");
        }

        var apiKey = _configuration["DeepDrftContent:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("DeepDrftContent:ApiKey is not configured");
            return StatusCode(500, "Content API key is not configured");
        }

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdValue, out var userId))
        {
            // [Authorize(Roles = "Admin")] gates upstream, so a missing/unparseable
            // user id here is a configuration bug, not a normal client state.
            _logger.LogError("Authenticated user has no parseable NameIdentifier claim: {Value}", userIdValue);
            return StatusCode(500, "Authenticated user is missing a valid identifier");
        }

        // Forward the upload to DeepDrftContent. We rebuild the multipart container rather
        // than relaying Request.Body so the boundary is owned by HttpClient and IFormFile's
        // already-buffered stream (memory + temp-file backed by Kestrel) is the source.
        using var multipart = new MultipartFormDataContent();
        await using var wavStream = wav.OpenReadStream();
        var wavContent = new StreamContent(wavStream);
        wavContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(wav.ContentType) ? "audio/wav" : wav.ContentType);
        multipart.Add(wavContent, "wav", wav.FileName);
        multipart.Add(new StringContent(trackName), "trackName");
        multipart.Add(new StringContent(artist), "artist");
        if (!string.IsNullOrWhiteSpace(album)) multipart.Add(new StringContent(album), "album");
        if (!string.IsNullOrWhiteSpace(genre)) multipart.Add(new StringContent(genre), "genre");
        if (!string.IsNullOrWhiteSpace(releaseDate)) multipart.Add(new StringContent(releaseDate), "releaseDate");

        var client = _httpClientFactory.CreateClient(ContentClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, UploadPath) { Content = multipart };
        request.Headers.Add("ApiKey", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content API call failed for upload of {TrackName}", trackName);
            return StatusCode(502, "Content API is unreachable");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;
                if (statusCode >= 500)
                {
                    _logger.LogError("Content API returned {Status} for upload of {TrackName}: {Body}", statusCode, trackName, body);
                    return StatusCode(statusCode, "Upload failed on the content server. Please try again.");
                }

                // 4xx: body is user-friendly validation text from DeepDrftContent — relay as-is.
                _logger.LogWarning("Content API rejected upload: {Status} {Body}", statusCode, body);
                return StatusCode(statusCode, body);
            }

            TrackEntity? unpersisted;
            try
            {
                unpersisted = await response.Content.ReadFromJsonAsync<TrackEntity>(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize TrackEntity from Content API response");
                return StatusCode(502, "Content API returned an unexpected response");
            }

            if (unpersisted is null)
            {
                _logger.LogError("Content API returned a null TrackEntity");
                return StatusCode(502, "Content API returned an empty response");
            }

            unpersisted.CreatedByUserId = userId;

            var saveResult = await _trackService.Create(unpersisted);
            if (!saveResult.Success || saveResult.Value is null)
            {
                // The vault write succeeded but the SQL persist failed — audio is now orphaned
                // in the tracks vault under EntryKey. CMS-PLAN W2.4 covers the dead-letter
                // mechanism; until then we log loudly so the orphan is recoverable manually.
                var error = saveResult.Messages.FirstOrDefault()?.Message ?? "Unknown error";
                _logger.LogError(
                    "Track persisted to vault but SQL save failed. Orphaned entry: {EntryKey}. Error: {Error}",
                    unpersisted.EntryKey, error);
                return StatusCode(500, $"Track was uploaded but could not be saved: {error}");
            }

            return Ok(saveResult.Value);
        }
    }
}
