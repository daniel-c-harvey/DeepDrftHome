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
    private readonly DeepDrftContent.Services.TrackService _trackService;
    private readonly WavOffsetService _wavOffsetService;
    private readonly ILogger<TrackController> _logger;

    // FileDatabase is injected directly for PutTrack because that endpoint receives a pre-processed
    // AudioBinaryDto over the wire, not a WAV file path. TrackService.AddTrackFromWavAsync is
    // file-path-oriented and not applicable here. If a file-upload flow is added in future,
    // route it through TrackService instead.
    private readonly DeepDrftContent.Services.FileDatabase.Services.FileDatabase _fileDatabase;

    public TrackController(
        DeepDrftContent.Services.TrackService trackService,
        DeepDrftContent.Services.FileDatabase.Services.FileDatabase fileDatabase,
        WavOffsetService wavOffsetService,
        ILogger<TrackController> logger)
    {
        _trackService = trackService;
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

            // Offset path: route through TrackService.GetAudioBinaryAsync (Track B's
            // orchestrator boundary) so the controller stays out of FileDatabase directly.
            // The buffered AudioBinary is required because WavOffsetService block-aligns
            // and reslices into a composite stream over the in-memory buffer.
            var file = await _trackService.GetAudioBinaryAsync(trackId);
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

    // POST api/track/upload: raw WAV in (multipart/form-data) + metadata → unpersisted TrackEntity out.
    // Used by the CMS upload flow on DeepDrftWeb; that host proxies the upload here so it never
    // touches the vault disk path directly (Option B in CMS-PLAN §5).
    //
    // RequestSizeLimit/MultipartBodyLengthLimit set to 1 GB: WAV uploads can be tens to hundreds
    // of MB and the framework defaults (~28 MB) reject them outright. The IFormFile path streams
    // the body to a temp file once Kestrel surfaces it, so the limit is the per-request ceiling,
    // not a buffered allocation.
    [ApiKeyAuthorize]
    [HttpPost("upload")]
    [RequestSizeLimit(1_073_741_824)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_073_741_824)]
    public async Task<ActionResult<DeepDrftModels.Entities.TrackEntity>> UploadTrack(
        [FromForm] IFormFile? wav,
        [FromForm] string? trackName,
        [FromForm] string? artist,
        [FromForm] string? album,
        [FromForm] string? genre,
        [FromForm] string? releaseDate,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("UploadTrack called: trackName={TrackName}, artist={Artist}, size={Size}",
            trackName, artist, wav?.Length);

        if (wav is null || wav.Length == 0)
        {
            return BadRequest("WAV file is required");
        }

        if (string.IsNullOrWhiteSpace(trackName))
        {
            return BadRequest("trackName is required");
        }

        if (string.IsNullOrWhiteSpace(artist))
        {
            return BadRequest("artist is required");
        }

        if (!string.Equals(Path.GetExtension(wav.FileName), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Uploaded file must have a .wav extension");
        }

        DateOnly? parsedReleaseDate = null;
        if (!string.IsNullOrWhiteSpace(releaseDate))
        {
            if (!DateOnly.TryParseExact(releaseDate, "yyyy-MM-dd", out var parsed))
            {
                return BadRequest("releaseDate must be in YYYY-MM-DD format");
            }
            parsedReleaseDate = parsed;
        }

        // AudioProcessor.ProcessWavFileAsync requires a path ending in .wav and reads from disk.
        // Path.GetTempFileName() yields .tmp, which fails that check — generate our own .wav path.
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wav");

        try
        {
            await using (var tempStream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            await using (var uploadStream = wav.OpenReadStream())
            {
                await uploadStream.CopyToAsync(tempStream, cancellationToken);
            }

            var entity = await _trackService.AddTrackFromWavAsync(
                tempPath,
                trackName,
                artist,
                string.IsNullOrWhiteSpace(album) ? null : album,
                string.IsNullOrWhiteSpace(genre) ? null : genre,
                parsedReleaseDate);

            if (entity is null)
            {
                _logger.LogWarning("UploadTrack: TrackService returned null for {TrackName}", trackName);
                return StatusCode(500, "Failed to process and store WAV");
            }

            _logger.LogInformation("UploadTrack succeeded: entryKey={EntryKey}", entity.EntryKey);
            return Ok(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadTrack failed for {TrackName}", trackName);
            return StatusCode(500, "Internal server error");
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UploadTrack: failed to delete temp file {TempPath}", tempPath);
            }
        }
    }

    [ApiKeyAuthorize]
    [HttpPut("{trackId}")]
    public async Task<ActionResult> PutTrack(string trackId, [FromBody] AudioBinaryDto track)
    {
        _logger.LogInformation("PutTrack called with trackId: {TrackId}", trackId);

        // Reject unknown MIME types up front rather than silently storing the binary
        // with a ".bin" extension. GetExtension returns ".bin" for any unrecognised
        // MIME, so treat that as the sentinel for an unsupported type.
        if (MimeTypeExtensions.GetExtension(track.Mime) == ".bin")
        {
            _logger.LogWarning("PutTrack rejected: unsupported MIME type '{Mime}' for track {TrackId}", track.Mime, trackId);
            return BadRequest($"Unsupported MIME type: {track.Mime}");
        }

        var audioBinary = AudioBinary.From(track);
        // Direct FileDatabase write: this endpoint receives an already-processed AudioBinaryDto,
        // not a WAV file, so TrackService.AddTrackFromWavAsync does not apply. See constructor comment.
        var success = await _fileDatabase.RegisterResourceAsync(
            DeepDrftContent.Services.Constants.VaultConstants.Tracks, trackId, audioBinary);
        return success ? Ok() : BadRequest("Failed to store audio track");
    }

    [ApiKeyAuthorize]
    [HttpDelete("{entryKey}")]
    public async Task<ActionResult> DeleteTrack(string entryKey)
    {
        _logger.LogInformation("DeleteTrack called with entryKey: {EntryKey}", entryKey);

        // RemoveResourceAsync distinguishes three outcomes per FileDatabase's error-swallow contract:
        //   null  → vault missing or unexpected error → 500
        //   false → entry not present (already deleted or never existed) → 404
        //   true  → entry removed → 200
        var outcome = await _fileDatabase.RemoveResourceAsync(VaultConstants.Tracks, entryKey);
        if (outcome == null)
        {
            _logger.LogError("DeleteTrack failed for entryKey: {EntryKey} (vault missing or remove error)", entryKey);
            return StatusCode(500, "Internal server error");
        }

        if (outcome == false)
        {
            _logger.LogWarning("DeleteTrack: entry not found: {EntryKey}", entryKey);
            return NotFound();
        }

        _logger.LogInformation("DeleteTrack: removed entry {EntryKey}", entryKey);
        return Ok();
    }
}
