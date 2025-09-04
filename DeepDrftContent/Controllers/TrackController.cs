using DeepDrftContent.Constants;
using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Services;
using DeepDrftContent.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace DeepDrftContent.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrackController : ControllerBase
{
    private readonly FileDatabase.Services.FileDatabase _fileDatabase;

    public TrackController(FileDatabase.Services.FileDatabase fileDatabase)
    {
        _fileDatabase = fileDatabase;
    }

    [HttpGet("{trackId}")]
    public async Task<ActionResult> GetTrack(string trackId)
    {
        var file = await _fileDatabase.LoadResourceAsync<AudioBinary>(VaultConstants.Tracks, trackId);
        if (file == null) { return NotFound(); }
        return File(file.Buffer, MimeTypeExtensions.GetMimeType(file.Extension));
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