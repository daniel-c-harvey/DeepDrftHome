using Microsoft.AspNetCore.Components;

namespace DeepDrftWeb.Client.Common;

public class DarkModeSettings()
{
    [PersistentState]
    public bool IsDarkMode
    {
        get;
        set
        {
            if (value == field) return;
            field = value;
        }
    } = false;
}