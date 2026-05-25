using DeepDrftContent.FileDatabase.Models;

namespace DeepDrftTests;

/// <summary>
/// Test data and helper methods for FileDatabase tests
/// </summary>
public static class TestData
{
    /// <summary>
    /// Test PNG image bytes (16x16 pixel test image)
    /// </summary>
    public static readonly byte[] TestPngBytes = 
    [
        137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,0,16,0,0,0,16,8,6,0,0,0,31,243,255,97,0,0,2,8,73,68,65,84,56,203,61,144,59,142,28,71,16,5,163,42,95,254,186,103,185,182,100,233,34,188,255,13,116,0,57,50,36,136,132,86,208,252,187,105,244,112,141,0,10,168,202,120,47,107,12,255,117,127,151,243,238,193,23,79,78,158,172,30,156,34,89,221,63,121,11,231,45,130,85,226,45,156,245,133,84,65,41,88,60,41,79,42,146,142,36,37,202,157,114,167,93,44,17,148,59,75,56,229,162,195,89,66,104,205,100,81,146,94,100,36,25,65,122,80,210,241,208,197,226,241,57,176,132,179,186,88,226,184,211,26,69,69,145,241,18,184,31,152,81,46,22,119,58,244,226,72,175,16,237,199,89,89,141,103,99,158,120,36,225,78,201,104,25,139,139,118,99,137,67,242,51,181,253,144,151,59,82,47,140,108,70,20,195,19,73,132,77,74,147,142,73,135,209,110,116,56,29,70,73,164,139,140,99,69,61,151,230,81,43,215,104,206,42,78,22,164,61,105,219,233,216,233,16,229,131,138,73,134,145,18,41,163,234,144,234,122,42,182,116,238,17,92,50,56,231,23,108,44,252,114,255,155,37,110,116,12,50,94,130,87,245,116,35,195,201,16,186,182,177,229,100,203,13,234,201,165,190,243,71,125,112,169,255,248,122,189,179,142,70,74,36,144,6,18,71,147,112,50,133,110,109,88,14,72,176,222,176,20,214,131,71,173,252,254,126,229,60,225,55,118,122,3,38,184,12,133,161,152,40,28,109,109,108,49,25,101,140,50,172,133,149,177,231,228,82,193,95,177,99,115,199,24,36,147,155,193,110,131,41,3,159,232,94,162,202,217,83,108,37,248,196,120,150,113,139,39,31,190,243,167,6,49,141,192,56,153,193,52,144,161,75,137,46,103,235,128,20,148,51,90,140,114,246,156,220,3,46,1,231,24,252,163,65,75,244,16,235,48,22,51,68,59,143,242,35,189,3,202,217,211,217,75,108,105,60,115,242,204,201,45,7,255,199,224,187,79,150,57,89,205,169,97,232,231,208,172,96,235,96,79,135,118,246,16,123,138,61,141,189,140,71,78,110,101,156,125,240,175,15,190,233,104,33,218,121,118,176,101,48,94,130,45,197,94,130,116,40,177,229,241,47,91,192,163,156,75,76,62,124,240,109,12,126,0,211,140,106,253,37,95,60,102,0,0,0,0,73,69,78,68,174,66,96,130
    ];

    /// <summary>
    /// Creates a test ImageBinary with the test PNG data
    /// </summary>
    /// <param name="aspectRatio">The aspect ratio for the image</param>
    /// <returns>An ImageBinary instance with test data</returns>
    public static ImageBinary CreateTestImageBinary(double aspectRatio = 1.0)
    {
        var parameters = new ImageBinaryParams(
            Buffer: TestPngBytes,
            Size: TestPngBytes.Length,
            Extension: ".png",
            AspectRatio: aspectRatio
        );
        return new ImageBinary(parameters);
    }

    /// <summary>
    /// Creates a test AudioBinary with mock audio data
    /// </summary>
    /// <param name="duration">The duration in seconds</param>
    /// <param name="bitrate">The bitrate in kbps</param>
    /// <returns>An AudioBinary instance with test data</returns>
    public static AudioBinary CreateTestAudioBinary(double duration = 120.0, int bitrate = 320)
    {
        // Using PNG bytes as mock audio data for testing purposes
        var parameters = new AudioBinaryParams(
            Buffer: TestPngBytes,
            Size: TestPngBytes.Length,
            Extension: ".mp3",
            Duration: duration,
            Bitrate: bitrate
        );
        return new AudioBinary(parameters);
    }

    /// <summary>
    /// Test keys used across tests - now using simple strings and separate vault types
    /// </summary>
    public static class TestKeys
    {
        public const string TestImageEntry = "test";
        public const string ImageVaultKey = "img"; 
        public const string NonExistentVaultKey = "i-do-not-exist";
        public const string NonExistentEntryKey = "i-do-not-exist";
        
        // Vault type for image-related tests
        public const MediaVaultType ImageVaultType = MediaVaultType.Image;
    }

    /// <summary>
    /// Test file names
    /// </summary>
    public static class TestFiles
    {
        public const string TestPngName = "test.png";
    }
}
