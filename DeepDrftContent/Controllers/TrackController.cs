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
    private readonly ILogger<TrackController> _logger;

    public TrackController(DeepDrftContent.Services.FileDatabase.Services.FileDatabase fileDatabase, ILogger<TrackController> logger)
    {
        _fileDatabase = fileDatabase;
        _logger = logger;
    }

    [HttpGet("{trackId}")]
    public async Task<ActionResult> GetTrack(string trackId)
    {
        _logger.LogInformation("GetTrack called with trackId: {TrackId}", trackId);
        
        try
        {
            var file = await _fileDatabase.LoadResourceAsync<AudioBinary>(VaultConstants.Tracks, trackId);
            if (file == null) 
            { 
                _logger.LogWarning("Track not found: {TrackId}", trackId);
                return NotFound(); 
            }

            _logger.LogInformation("Successfully retrieved track: {TrackId}, Size: {Size} bytes", trackId, file.Buffer.Length);
            return File(file.Buffer, MimeTypeExtensions.GetMimeType(file.Extension));
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