using DeepDrftContent.Services.Audio;
using DeepDrftContent.Services.Constants;
using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Services.FileDatabase.Services;
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
            // No-offset path: stream the file straight from disk so a 100 MB WAV does not
            // force a 100 MB LOH allocation per request. The offset path still loads
            // the full buffer because WavOffsetService block-aligns and reslices into
            // a composite stream over the in-memory buffer.
            if (offset == 0)
            {
                var vault = _fileDatabase.GetVault(VaultConstants.Tracks);
                if (vault == null)
                {
                    _logger.LogWarning("Tracks vault not found");
                    return NotFound();
                }

                var mediaStream = await vault.GetEntryStreamAsync(trackId);
                if (mediaStream == null)
                {
                    _logger.LogWarning("Track not found: {TrackId}", trackId);
                    return NotFound();
                }

                // Resolve MIME and log before handing the stream to File().
                // If anything here throws, the finally block disposes the wrapper
                // (and its inner FileStream) so neither leaks. On the success path
                // File() takes ownership of the inner stream; ASP.NET Core disposes
                // it after the response body is sent. The wrapper is a thin struct
                // with no extra resources, so disposing it after extracting the
                // inner stream is a no-op — we only call Dispose() in the catch path.
                string streamMimeType;
                long streamLength;
                Stream innerStream;
                try
                {
                    streamMimeType = MimeTypeExtensions.GetMimeType(mediaStream.Extension);
                    streamLength = mediaStream.Stream.Length;
                    innerStream = mediaStream.Stream;
                }
                catch
                {
                    await mediaStream.DisposeAsync();
                    throw;
                }

                _logger.LogInformation(
                    "Streaming track from disk: {TrackId}, Size: {Size} bytes",
                    trackId, streamLength);
                // enableRangeProcessing: false — seek is served by WavOffsetService, not Range.
                return File(innerStream, streamMimeType, enableRangeProcessing: false);
            }

            // Offset path: buffer the file (current behaviour) and synthesise a
            // header-prefixed slice via WavOffsetService.
            var file = await _fileDatabase.LoadResourceAsync<AudioBinary>(VaultConstants.Tracks, trackId);
            if (file == null)
            {
                _logger.LogWarning("Track not found: {TrackId}", trackId);
                return NotFound();
            }

            var mimeType = MimeTypeExtensions.GetMimeType(file.Extension);

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
