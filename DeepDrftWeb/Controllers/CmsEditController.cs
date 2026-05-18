using DeepDrftModels.Entities;
using DeepDrftWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetBlocks.Models;

namespace DeepDrftWeb.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/cms/track")]
public class CmsEditController : ControllerBase
{
    private readonly ITrackService _trackService;

    public CmsEditController(ITrackService trackService)
    {
        _trackService = trackService;
    }

    // Metadata-only update. EntryKey is immutable in Wave 1 — audio replacement
    // is a separate Wave 2 operation that touches the vault.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResultDto<TrackEntity>>> Update(int id, [FromBody] CmsTrackUpdateRequest request)
    {
        var existing = await _trackService.GetById(id);
        if (!existing.Success)
        {
            var failure = ApiResult<TrackEntity>.CreateFailResult(existing.GetMessage());
            return StatusCode(500, new ApiResultDto<TrackEntity>(failure));
        }

        if (existing.Value is null)
        {
            return NotFound();
        }

        var track = existing.Value;
        track.TrackName = request.TrackName;
        track.Artist = request.Artist;
        track.Album = request.Album;
        track.Genre = request.Genre;
        track.ReleaseDate = request.ReleaseDate;

        var updated = await _trackService.Update(track);
        var apiResult = ApiResult<TrackEntity>.From(updated);
        var dto = new ApiResultDto<TrackEntity>(apiResult);

        return updated.Success ? Ok(dto) : StatusCode(500, dto);
    }
}

public record CmsTrackUpdateRequest(
    string TrackName,
    string Artist,
    string? Album,
    string? Genre,
    DateOnly? ReleaseDate);
