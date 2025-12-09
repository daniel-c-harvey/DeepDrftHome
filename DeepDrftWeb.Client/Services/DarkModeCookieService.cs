using DeepDrftWeb.Client.Common;
using Microsoft.JSInterop;

namespace DeepDrftWeb.Client.Services;

public class DarkModeCookieService(DarkModeSettings darkModeSetting, IJSRuntime js) : DarkModeServiceBase
{
    private const int EXPIRY_DAYS = 365;

    public bool GetDarkModeAsync()
    {
        return darkModeSetting.IsDarkMode;
        // var value = await js.InvokeAsync<string?>("eval", 
        //     $"document.cookie.split('; ').find(c => c.startsWith('{COOKIE_NAME}='))?.split('=')[1]");
        // return value == "true";
    }

    public async ValueTask SetDarkModeAsync(bool isDarkMode)
    {
        var expires = DateTime.UtcNow.AddDays(EXPIRY_DAYS).ToString("R");
        await js.InvokeVoidAsync("eval", 
            $"document.cookie = '{COOKIE_NAME}={isDarkMode.ToString().ToLower()}; expires={expires}; path=/; SameSite=Lax'");
        darkModeSetting.IsDarkMode = isDarkMode;
    }
}