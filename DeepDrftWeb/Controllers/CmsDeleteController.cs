using DeepDrftData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeepDrftWeb.Controllers;

/// <summary>
/// CMS delete endpoint. Owned by W3-T3 — separate controller from upload/edit to
/// avoid merge contention with parallel CMS tracks.
///
/// Delete order (CMS-PLAN W1.5): SQL first, then vault. If the SQL row is gone we
/// return success to the user even when the subsequent vault delete fails — SQL is
/// the source of truth for "exists from the user's view". A vault failure is logged
/// as an orphan for maintenance to reap (see PLAN.md §4.3 dead-letter).
/// </summary>
[ApiController]
[Route("api/cms/track")]
[Authorize(Roles = "Admin")]
public class CmsDeleteController : ControllerBase
{
    private readonly ITrackService _trackService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CmsDeleteController> _logger;

    public CmsDeleteController(
        ITrackService trackService,
        IHttpClientFactory httpClientFactory,
        ILogger<CmsDeleteController> logger)
    {
        _trackService = trackService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteTrack(long id)
    {
        // 1. Resolve the EntryKey before we delete the SQL row — afterwards the join is gone.
        var lookup = await _trackService.GetById(id);
        if (!lookup.Success)
        {
            _logger.LogError("CMS delete: lookup failed for track {TrackId}: {Error}", id, lookup.Messages.FirstOrDefault()?.Message);
            return StatusCode(500, "Failed to load track");
        }

        var track = lookup.Value;
        if (track == null)
        {
            return NotFound();
        }

        var entryKey = track.EntryKey;

        // 2. SQL delete. On failure, do NOT touch the vault — nothing to clean up.
        var sqlDelete = await _trackService.Delete(id);
        if (!sqlDelete.Success)
        {
            _logger.LogError("CMS delete: SQL delete failed for track {TrackId}: {Error}", id, sqlDelete.Messages.FirstOrDefault()?.Message);
            return StatusCode(500, "Failed to delete track");
        }

        // 3. Vault delete. Failure is logged as an orphan but does not fail the request:
        //    SQL is the source of truth for the user's view; the orphan is a maintenance concern.
        var client = _httpClientFactory.CreateClient(Startup.ContentCmsHttpClientName);
        try
        {
            var response = await client.DeleteAsync($"api/track/{Uri.EscapeDataString(entryKey)}");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Vault delete failed after SQL delete. {TrackId} {EntryKey} {StatusCode}",
                    id, entryKey, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Vault delete threw after SQL delete. {TrackId} {EntryKey}",
                id, entryKey);
        }

        return Ok();
    }
}
