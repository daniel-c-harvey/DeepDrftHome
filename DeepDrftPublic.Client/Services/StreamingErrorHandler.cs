using Microsoft.Extensions.Logging;

namespace DeepDrftPublic.Client.Services;

public static class StreamingErrorHandler
{
    public static string GetUserFriendlyMessage(string technicalError)
    {
        var lowerError = technicalError.ToLowerInvariant();
        
        return lowerError switch
        {
            _ when lowerError.Contains("network") || lowerError.Contains("connection") || lowerError.Contains("timeout") =>
                "Unable to load audio. Please check your connection and try again.",

            _ when lowerError.Contains("header") || lowerError.Contains("wav") || lowerError.Contains("invalid wav") =>
                "This file format is not supported. Only WAV files can be played.",

            _ when lowerError.Contains("audio") || lowerError.Contains("decode") || lowerError.Contains("format") =>
                "This audio file may be corrupted or in an unsupported format.",

            _ when lowerError.Contains("cancel") || lowerError.Contains("abort") =>
                "Audio loading was cancelled.",

            _ => "Unable to play audio. Please try again."
        };
    }
    
    public static void LogError(ILogger logger, Exception ex, string operation, string trackId = "")
    {
        logger.LogError(ex, "Streaming error in {Operation} for track {TrackId}", operation, trackId);
    }
}