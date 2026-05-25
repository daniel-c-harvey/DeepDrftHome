using System.Net.Http.Headers;
using DeepDrftData;
using DeepDrftModels.Entities;
using NetBlocks.Models;

namespace DeepDrftManager.Services;

/// <summary>
/// Direct-call CMS track service. Replaces the former in-process HTTP roundtrip through
/// CmsUploadController / CmsDeleteController: the Manager is InteractiveServer-only, so its
/// Blazor components inject this service and call it directly rather than POSTing to their
/// own loopback controllers. Vault access remains over HTTP to DeepDrftContent (a separate
/// host); SQL metadata is reached directly via <see cref="ITrackService"/>.
/// </summary>
public class CmsTrackService : ICmsTrackService
{
    private const string ContentClientName = "DeepDrft.Content";
    private const string ContentCmsClientName = "DeepDrft.Content.Cms";
    private const string UploadPath = "api/track/upload";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITrackService _trackService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CmsTrackService> _logger;

    public CmsTrackService(
        IHttpClientFactory httpClientFactory,
        ITrackService trackService,
        IConfiguration configuration,
        ILogger<CmsTrackService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _trackService = trackService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ResultContainer<TrackEntity>> UploadTrackAsync(
        Stream wavStream,
        string fileName,
        string contentType,
        string trackName,
        string artist,
        string? album,
        string? genre,
        string? releaseDate,
        long createdByUserId,
        CancellationToken ct = default)
    {
        var apiKey = _configuration["DeepDrftContent:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("DeepDrftContent:ApiKey is not configured");
            return ResultContainer<TrackEntity>.CreateFailResult("Content API key is not configured.");
        }

        // Rebuild the multipart container so the boundary is owned by HttpClient and the
        // caller-supplied stream (already buffered by the SignalR upload) is the source.
        using var multipart = new MultipartFormDataContent();
        var wavContent = new StreamContent(wavStream);
        wavContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "audio/wav" : contentType);
        multipart.Add(wavContent, "wav", fileName);
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
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content API call failed for upload of {TrackName}", trackName);
            return ResultContainer<TrackEntity>.CreateFailResult("Content API is unreachable.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var statusCode = (int)response.StatusCode;
                if (statusCode >= 500)
                {
                    _logger.LogError("Content API returned {Status} for upload of {TrackName}: {Body}", statusCode, trackName, body);
                    return ResultContainer<TrackEntity>.CreateFailResult("Upload failed on the content server. Please try again.");
                }

                // 4xx: body is user-friendly validation text from DeepDrftContent — relay as-is.
                _logger.LogWarning("Content API rejected upload: {Status} {Body}", statusCode, body);
                return ResultContainer<TrackEntity>.CreateFailResult(
                    string.IsNullOrWhiteSpace(body) ? $"Upload rejected ({statusCode})." : body);
            }

            TrackEntity? unpersisted;
            try
            {
                unpersisted = await response.Content.ReadFromJsonAsync<TrackEntity>(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize TrackEntity from Content API response");
                return ResultContainer<TrackEntity>.CreateFailResult("Content API returned an unexpected response.");
            }

            if (unpersisted is null)
            {
                _logger.LogError("Content API returned a null TrackEntity");
                return ResultContainer<TrackEntity>.CreateFailResult("Content API returned an empty response.");
            }

            unpersisted.CreatedByUserId = createdByUserId;

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
                return ResultContainer<TrackEntity>.CreateFailResult($"Track was uploaded but could not be saved: {error}");
            }

            return saveResult;
        }
    }

    public async Task<Result> DeleteTrackAsync(long id, CancellationToken ct = default)
    {
        // 1. Resolve the EntryKey before we delete the SQL row — afterwards the join is gone.
        var lookup = await _trackService.GetById(id);
        if (!lookup.Success)
        {
            var error = lookup.Messages.FirstOrDefault()?.Message ?? "unknown error";
            _logger.LogError("CMS delete: GetById threw for track {TrackId}: {Error}", id, error);
            return Result.CreateFailResult("Failed to load track.");
        }

        if (lookup.Value is null)
        {
            return Result.CreateFailResult("Track not found.");
        }

        var track = lookup.Value;

        var entryKey = track.EntryKey;

        // 2. SQL delete. On failure, do NOT touch the vault — nothing to clean up.
        var sqlDelete = await _trackService.Delete(id);
        if (!sqlDelete.Success)
        {
            var error = sqlDelete.Messages.FirstOrDefault()?.Message;
            _logger.LogError("CMS delete: SQL delete failed for track {TrackId}: {Error}", id, error);
            return Result.CreateFailResult("Failed to delete track.");
        }

        // 3. Vault delete. Failure is logged as an orphan but does not fail the operation:
        //    SQL is the source of truth for the user's view; the orphan is a maintenance concern.
        var client = _httpClientFactory.CreateClient(ContentCmsClientName);
        try
        {
            using var response = await client.DeleteAsync($"api/track/{Uri.EscapeDataString(entryKey)}", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Vault delete failed after SQL delete. {TrackId} {EntryKey} {StatusCode}",
                    id, entryKey, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vault delete threw after SQL delete. {TrackId} {EntryKey}", id, entryKey);
        }

        return Result.CreatePassResult();
    }
}
