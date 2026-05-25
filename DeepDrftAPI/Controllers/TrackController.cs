using DeepDrftAPI.Middleware;
using DeepDrftAPI.Models;
using DeepDrftAPI.Services;
using DeepDrftContent.Audio;
using DeepDrftContent.Constants;
using DeepDrftContent.FileDatabase.Services;
using DeepDrftContent.FileDatabase.Models;
using DeepDrftData;
using Microsoft.AspNetCore.Mvc;

namespace DeepDrftAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrackController : ControllerBase
{
    private readonly DeepDrftContent.TrackContentService _trackContentService;
    private readonly WavOffsetService _wavOffsetService;
    private readonly UnifiedTrackService _unifiedService;
    private readonly ITrackService _sqlTrackService;
    private readonly ILogger<TrackController> _logger;

    // FileDatabase is injected directly for PutTrack because that endpoint receives a pre-processed
    // AudioBinaryDto over the wire, not a WAV file path. TrackContentService.AddTrackFromWavAsync is
    // file-path-oriented and not applicable here. If a file-upload flow is added in future,
    // route it through TrackContentService instead.
    private readonly DeepDrftContent.FileDatabase.Services.FileDatabase _fileDatabase;

    public TrackController(
        DeepDrftContent.TrackContentService trackContentService,
        DeepDrftContent.FileDatabase.Services.FileDatabase fileDatabase,
        WavOffsetService wavOffsetService,
        UnifiedTrackService unifiedService,
        ITrackService sqlTrackService,
        ILogger<TrackController> logger)
    {
        _trackContentService = trackContentService;
        _fileDatabase = fileDatabase;
        _wavOffsetService = wavOffsetService;
        _unifiedService = unifiedService;
        _sqlTrackService = sqlTrackService;
        _logger = logger;
    }

    // --- Literal-segment routes first ---
    // These are declared before the parameterized "{trackId}" / "{id:long}" actions so route
    // resolution never treats "page", "upload", or "meta" as a trackId.

    // GET api/track/page?page=1&pageSize=20&sortColumn=TrackName&sortDescending=false
    // CMS metadata listing — paged read straight from SQL.
    [ApiKeyAuthorize]
    [HttpGet("page")]
    public async Task<ActionResult> GetPage(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortColumn = null,
        [FromQuery] bool sortDescending = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _sqlTrackService.GetPaged(page, pageSize, sortColumn, sortDescending, cancellationToken);
        if (!result.Success || result.Value is null)
        {
            var error = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
            _logger.LogError("GetPage failed: {Error}", error);
            return StatusCode(500, "Failed to load tracks");
        }

        return Ok(result.Value);
    }

    // POST api/track/upload: raw WAV in (multipart/form-data) + metadata → persisted TrackEntity out.
    // Used by the CMS upload flow on DeepDrftManager; that host proxies the upload here so it never
    // touches the vault disk path or SQL directly. UnifiedTrackService owns the two-database write.
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
        [FromForm] long createdByUserId,
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

            var result = await _unifiedService.UploadAsync(
                tempPath,
                trackName,
                artist,
                string.IsNullOrWhiteSpace(album) ? null : album,
                string.IsNullOrWhiteSpace(genre) ? null : genre,
                parsedReleaseDate,
                createdByUserId,
                cancellationToken);

            if (!result.Success || result.Value is null)
            {
                var error = result.Messages.FirstOrDefault()?.Message ?? "Failed to process and store WAV";
                _logger.LogWarning("UploadTrack: UnifiedTrackService failed for {TrackName}: {Error}", trackName, error);
                return StatusCode(500, error);
            }

            _logger.LogInformation("UploadTrack succeeded: id={Id}, entryKey={EntryKey}", result.Value.Id, result.Value.EntryKey);
            return Ok(result.Value);
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

    // GET api/track/meta/{id}: single track metadata from SQL.
    [ApiKeyAuthorize]
    [HttpGet("meta/{id:long}")]
    public async Task<ActionResult> GetMeta(long id)
    {
        var result = await _sqlTrackService.GetById(id);
        if (!result.Success)
        {
            var error = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
            _logger.LogError("GetMeta failed for {TrackId}: {Error}", id, error);
            return StatusCode(500, "Failed to load track");
        }

        if (result.Value is null)
        {
            return NotFound();
        }

        return Ok(result.Value);
    }

    // PUT api/track/meta/{id}: metadata-only update. EntryKey is immutable and not part of the body.
    [ApiKeyAuthorize]
    [HttpPut("meta/{id:long}")]
    public async Task<ActionResult> UpdateMeta(long id, [FromBody] UpdateTrackMetadataRequest request)
    {
        var lookup = await _sqlTrackService.GetById(id);
        if (!lookup.Success)
        {
            var error = lookup.Messages.FirstOrDefault()?.Message ?? "Unknown error";
            _logger.LogError("UpdateMeta lookup failed for {TrackId}: {Error}", id, error);
            return StatusCode(500, "Failed to load track");
        }

        if (lookup.Value is null)
        {
            return NotFound();
        }

        var track = lookup.Value;
        track.TrackName = request.TrackName;
        track.Artist = request.Artist;
        track.Album = request.Album;
        track.Genre = request.Genre;
        track.ReleaseDate = request.ReleaseDate;

        var update = await _sqlTrackService.Update(track);
        if (!update.Success)
        {
            var error = update.Messages.FirstOrDefault()?.Message ?? "Unknown error";
            _logger.LogError("UpdateMeta failed for {TrackId}: {Error}", id, error);
            return StatusCode(500, "Failed to update track");
        }

        return Ok();
    }

    // DELETE api/track/{id}: removes the SQL row then the vault entry. UnifiedTrackService owns
    // the ordering and orphan handling. Declared (with the long route constraint) before the
    // string "{trackId}" GET so a numeric id routes here.
    [ApiKeyAuthorize]
    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteTrack(long id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteTrack called with id: {Id}", id);

        var result = await _unifiedService.DeleteAsync(id, cancellationToken);
        if (result.Success)
        {
            return Ok();
        }

        var error = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
        if (string.Equals(error, UnifiedTrackService.TrackNotFoundMessage, StringComparison.Ordinal))
        {
            return NotFound();
        }

        _logger.LogError("DeleteTrack failed for id {Id}: {Error}", id, error);
        return StatusCode(500, error);
    }

    // --- Parameterized routes ---

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

            // Offset path: route through TrackContentService.GetAudioBinaryAsync (Track B's
            // orchestrator boundary) so the controller stays out of FileDatabase directly.
            // The buffered AudioBinary is required because WavOffsetService block-aligns
            // and reslices into a composite stream over the in-memory buffer.
            var file = await _trackContentService.GetAudioBinaryAsync(trackId);
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
        // not a WAV file, so TrackContentService.AddTrackFromWavAsync does not apply. See constructor comment.
        var success = await _fileDatabase.RegisterResourceAsync(
            DeepDrftContent.Constants.VaultConstants.Tracks, trackId, audioBinary);
        return success ? Ok() : BadRequest("Failed to store audio track");
    }
}
