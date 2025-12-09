using Microsoft.AspNetCore.Components;

namespace DeepDrftWeb.Client.Common;

public class DarkModeSettings()
{
    // public EventCallback<bool> IsDarkModeChanged { get; set; }
    
    [PersistentState]
    public bool IsDarkMode
    {
        get;
        set
        {
            if (value == field) return;
            field = value;
            // IsDarkModeChanged.InvokeAsync(value);
        }
    } = false;
}