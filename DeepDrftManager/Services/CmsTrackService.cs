using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DeepDrftModels.DTOs;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftManager.Services;

/// <summary>
/// HTTP client over the DeepDrftAPI API for all CMS track operations. The Manager is
/// InteractiveServer-only and holds no in-process data layer: every track read and write is a
/// network call to DeepDrftAPI, which is the single authority over both the SQL metadata
/// store and the binary audio vault. The ApiKey is baked into the <c>DeepDrft.Content.Cms</c>
/// named client's default headers.
/// </summary>
public class CmsTrackService : ICmsTrackService
{
    private const string ContentCmsClientName = "DeepDrft.Content.Cms";
    private const string UploadPath = "api/track/upload";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CmsTrackService> _logger;

    public CmsTrackService(
        IHttpClientFactory httpClientFactory,
        ILogger<CmsTrackService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ResultContainer<TrackDto>> UploadTrackAsync(
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
        multipart.Add(new StringContent(createdByUserId.ToString()), "createdByUserId");

        var client = _httpClientFactory.CreateClient(ContentCmsClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, UploadPath) { Content = multipart };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content API call failed for upload of {TrackName}", trackName);
            return ResultContainer<TrackDto>.CreateFailResult("Content API is unreachable.");
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
                    return ResultContainer<TrackDto>.CreateFailResult("Upload failed on the content server. Please try again.");
                }

                // 4xx: body is user-friendly validation text from DeepDrftAPI — relay as-is.
                _logger.LogWarning("Content API rejected upload: {Status} {Body}", statusCode, body);
                return ResultContainer<TrackDto>.CreateFailResult(
                    string.IsNullOrWhiteSpace(body) ? $"Upload rejected ({statusCode})." : body);
            }

            // The Content API now owns the dual-database write, so the response is the persisted
            // track DTO (Id > 0) — no SQL roundtrip here.
            TrackDto? persisted;
            try
            {
                persisted = await response.Content.ReadFromJsonAsync<TrackDto>(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize TrackDto from Content API response");
                return ResultContainer<TrackDto>.CreateFailResult("Content API returned an unexpected response.");
            }

            if (persisted is null)
            {
                _logger.LogError("Content API returned a null TrackDto");
                return ResultContainer<TrackDto>.CreateFailResult("Content API returned an empty response.");
            }

            return ResultContainer<TrackDto>.CreatePassResult(persisted);
        }
    }

    public async Task<Result> DeleteTrackAsync(long id, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient(ContentCmsClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.DeleteAsync($"api/track/{id}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content API call failed for delete of track {TrackId}", id);
            return Result.CreateFailResult("Content API is unreachable.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return Result.CreatePassResult();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.CreateFailResult("Track not found.");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Content API delete failed for track {TrackId}: {Status} {Body}", id, (int)response.StatusCode, body);
            return Result.CreateFailResult("Failed to delete track.");
        }
    }

    public async Task<ResultContainer<PagedResult<TrackDto>>> GetPagedAsync(
        int page, int pageSize, string? sortColumn, bool sortDescending,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient(ContentCmsClientName);
        var query = $"api/track/page?page={page}&pageSize={pageSize}&sortDescending={sortDescending}";
        if (!string.IsNullOrWhiteSpace(sortColumn))
        {
            query += $"&sortColumn={Uri.EscapeDataString(sortColumn)}";
        }

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content API call failed for track page");
            return ResultContainer<PagedResult<TrackDto>>.CreateFailResult("Content API is unreachable.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Content API track page failed: {Status}", (int)response.StatusCode);
                return ResultContainer<PagedResult<TrackDto>>.CreateFailResult("Failed to load tracks.");
            }

            PagedResult<TrackDto>? paged;
            try
            {
                paged = await response.Content.ReadFromJsonAsync<PagedResult<TrackDto>>(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize PagedResult from Content API response");
                return ResultContainer<PagedResult<TrackDto>>.CreateFailResult("Content API returned an unexpected response.");
            }

            if (paged is null)
            {
                _logger.LogError("Content API returned a null PagedResult");
                return ResultContainer<PagedResult<TrackDto>>.CreateFailResult("Content API returned an empty response.");
            }

            return ResultContainer<PagedResult<TrackDto>>.CreatePassResult(paged);
        }
    }

    public async Task<ResultContainer<TrackDto?>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient(ContentCmsClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync($"api/track/meta/{id}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content API call failed for track {TrackId}", id);
            return ResultContainer<TrackDto?>.CreateFailResult("Content API is unreachable.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return ResultContainer<TrackDto?>.CreatePassResult(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Content API track lookup failed for {TrackId}: {Status}", id, (int)response.StatusCode);
                return ResultContainer<TrackDto?>.CreateFailResult("Failed to load track.");
            }

            TrackDto? track;
            try
            {
                track = await response.Content.ReadFromJsonAsync<TrackDto>(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize TrackDto from Content API response");
                return ResultContainer<TrackDto?>.CreateFailResult("Content API returned an unexpected response.");
            }

            return ResultContainer<TrackDto?>.CreatePassResult(track);
        }
    }

    public async Task<Result> UpdateAsync(
        long id, string trackName, string artist,
        string? album, string? genre, DateOnly? releaseDate,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient(ContentCmsClientName);
        var body = new
        {
            trackName,
            artist,
            album,
            genre,
            releaseDate,
        };

        HttpResponseMessage response;
        try
        {
            response = await client.PutAsJsonAsync($"api/track/meta/{id}", body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content API call failed for update of track {TrackId}", id);
            return Result.CreateFailResult("Content API is unreachable.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return Result.CreatePassResult();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.CreateFailResult("Track not found.");
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Content API update failed for track {TrackId}: {Status} {Body}", id, (int)response.StatusCode, responseBody);
            return Result.CreateFailResult("Failed to update track.");
        }
    }
}
