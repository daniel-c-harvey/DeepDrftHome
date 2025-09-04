using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Services;
using Microsoft.AspNetCore.Mvc;

namespace DeepDrftContent.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrackController : ControllerBase
{
    private readonly EntryKey _vaultKey = new("tracks", MediaVaultType.Audio);
    private readonly FileDatabase.Services.FileDatabase _fileDatabase;

    public TrackController(FileDatabase.Services.FileDatabase fileDatabase)
    {
        _fileDatabase = fileDatabase;
    }

    [HttpGet("{trackId}")]
    public async Task<ActionResult<AudioBinaryDto>> GetTrack([FromQuery] string trackId)
    {
        if (_fileDatabase.GetVault(_vaultKey) is not AudioVault vault) { return NotFound(); }
        var file = await vault.GetEntryAsync<AudioBinary>(MediaVaultType.Audio, new EntryKey(trackId, MediaVaultType.Audio));
        if (file == null) { return NotFound(); }
        return File(file.Buffer, MimeTypeExtensions.GetMimeType(file.Extension));
    }
}