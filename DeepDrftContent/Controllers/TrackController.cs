using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Services;
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
    public async Task<ActionResult<AudioBinaryDto>> GetTrack([FromQuery] string trackId)
    {
        // BEFORE: Complex with EntryKey objects and redundant MediaVaultType
        // var entryKey = new EntryKey(trackId, MediaVaultTypeMap.GetVaultType<AudioBinary>());
        // var file = await _fileDatabase.LoadResourceAsync<AudioBinary>(_vaultKey, entryKey);

        // AFTER: Ultra clean - just string identifiers, types inferred
        var file = await _fileDatabase.LoadResourceAsync<AudioBinary>("tracks", trackId);
        if (file == null) { return NotFound(); }
        return File(file.Buffer, MimeTypeExtensions.GetMimeType(file.Extension));
    }
}