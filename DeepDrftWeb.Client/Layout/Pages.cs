using MudBlazor;

namespace DeepDrftWeb.Client.Layout;

public class PageRoute
{
    public string Name { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string? Icon { get; set; } = null;
}

public static class Pages
{
    public static readonly List<PageRoute> MenuPages =
    [
        new() { Name = "Track Gallery", Route = "/tracks", Icon = Icons.Material.Filled.LibraryMusic }
    ];

    public static readonly List<PageRoute> AllPages = 
        new List<PageRoute>
        {
            new() { Name = "Home", Route = "/", Icon = Icons.Material.Filled.Home }
        }.Concat(MenuPages).ToList();
}