using DeepDrftContent.Services.Audio;
using DeepDrftContent.Services.Constants;
using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace DeepDrftContent.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrackController : ControllerBase
{
    private readonly DeepDrftContent.Services.FileDatabase.Services.FileDatabase _fileDatabase;
    private readonly WavOffsetService _wavOffsetService;
    private readonly ILogger<TrackController> _logger;

    public TrackController(
        DeepDrftContent.Services.FileDatabase.Services.FileDatabase fileDatabase,
        WavOffsetService wavOffsetService,
        ILogger<TrackController> logger)
    {
        _fileDatabase = fileDatabase;
        _wavOffsetService = wavOffsetService;
        _logger = logger;
    }

    [HttpGet("{trackId}")]
    public async Task<ActionResult> GetTrack(string trackId, [FromQuery] long offset = 0)
    {
        _logger.LogInformation("GetTrack called with trackId: {TrackId}, offset: {Offset}", trackId, offset);

        try
        {
            var file = await _fileDatabase.LoadResourceAsync<AudioBinary>(VaultConstants.Tracks, trackId);
            if (file == null)
            {
                _logger.LogWarning("Track not found: {TrackId}", trackId);
                return NotFound();
            }

            var mimeType = MimeTypeExtensions.GetMimeType(file.Extension);

            // If no offset, return the full file
            if (offset == 0)
            {
                _logger.LogInformation("Successfully retrieved track: {TrackId}, Size: {Size} bytes", trackId, file.Buffer.Length);
                return File(file.Buffer, mimeType);
            }

            // Create offset stream with synthesized header
            var offsetStream = _wavOffsetService.CreateOffsetStream(file.Buffer, offset);
            if (offsetStream == null)
            {
                _logger.LogWarning("Invalid offset {Offset} for track: {TrackId}", offset, trackId);
                return BadRequest("Invalid offset");
            }

            _logger.LogInformation("Successfully retrieved track with offset: {TrackId}, Offset: {Offset}, StreamSize: {Size} bytes",
                trackId, offset, offsetStream.Length);
            return File(offsetStream, mimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving track: {TrackId}", trackId);
            return StatusCode(500, "Internal server error");
        }
    }

    [ApiKeyAuthorize]
    [HttpPut("{trackId}")]
    public async Task<ActionResult> PutTrack(string trackId, [FromBody] AudioBinaryDto track)
    {
        _logger.LogInformation("PutTrack called with trackId: {TrackId}", trackId);
        var audioBinary = AudioBinary.From(track);
        var success = await _fileDatabase.RegisterResourceAsync(VaultConstants.Tracks, trackId, audioBinary);
        return success ? Ok() : BadRequest("Failed to store audio track");
    }
}