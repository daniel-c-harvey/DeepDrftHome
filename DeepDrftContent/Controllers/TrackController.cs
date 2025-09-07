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

    public TrackController(DeepDrftContent.Services.FileDatabase.Services.FileDatabase fileDatabase)
    {
        _fileDatabase = fileDatabase;
    }

    [HttpGet("{trackId}")]
    public async Task<ActionResult> GetTrack(string trackId)
    {
        var file = await _fileDatabase.LoadResourceAsync<AudioBinary>(VaultConstants.Tracks, trackId);
        if (file == null) { return NotFound(); }

        return File(file.Buffer, MimeTypeExtensions.GetMimeType(file.Extension), enableRangeProcessing: true);
    }

    [ApiKeyAuthorize]
    [HttpPut("{trackId}")]
    public async Task<ActionResult> PutTrack([FromQuery] string trackId, [FromBody] AudioBinaryDto track)
    {
        var audioBinary = AudioBinary.From(track);
        var success = await _fileDatabase.RegisterResourceAsync(VaultConstants.Tracks, trackId, audioBinary);
        return success ? Ok() : BadRequest("Failed to store audio track");
    }
}