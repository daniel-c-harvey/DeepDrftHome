using DeepDrftPublic.Client.Common;
using Microsoft.JSInterop;

namespace DeepDrftPublic.Client.Services;

public class DarkModeCookieService(DarkModeSettings darkModeSetting, IJSRuntime js) : DarkModeServiceBase
{
    private const int EXPIRY_DAYS = 365;

    public bool GetDarkMode()
    {
        return darkModeSetting.IsDarkMode;
    }

    public async ValueTask SetDarkModeAsync(bool isDarkMode)
    {
        var expires = DateTime.UtcNow.AddDays(EXPIRY_DAYS).ToString("R");
        await js.InvokeVoidAsync("eval", 
            $"document.cookie = '{COOKIE_NAME}={isDarkMode.ToString().ToLower()}; expires={expires}; path=/; SameSite=Lax'");
        darkModeSetting.IsDarkMode = isDarkMode;
    }
}