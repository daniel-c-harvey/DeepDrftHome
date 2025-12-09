using DeepDrftWeb.Client.Common;
using DeepDrftWeb.Client.Services;

namespace DeepDrftWeb.Services;

public class DarkModeService(DarkModeSettings darkModeSettings, IHttpContextAccessor httpAccessor) : DarkModeServiceBase
{
    public void CheckDarkMode()
    {
        // get
        // {
            bool isDarkMode = false; // Default to light mode
            var context = httpAccessor.HttpContext;
            if (context?.Request.Cookies.TryGetValue(COOKIE_NAME, out var dark) == true)
            {
                isDarkMode = dark == "true";
            }
            darkModeSettings.IsDarkMode = isDarkMode;
            // return isDarkMode;
        // }
    }
}