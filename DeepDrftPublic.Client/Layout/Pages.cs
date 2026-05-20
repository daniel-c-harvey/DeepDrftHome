using MudBlazor;

namespace DeepDrftPublic.Client.Layout;

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
        new() { Name = "Listen",   Route = "/tracks",   Icon = Icons.Material.Filled.LibraryMusic },
        new() { Name = "Sessions", Route = "#",         Icon = Icons.Material.Filled.Album },          // TODO: placeholder until Sessions ships
        new() { Name = "Archive",  Route = "#",         Icon = Icons.Material.Filled.FolderOpen },     // TODO: placeholder until Archive ships
        new() { Name = "About",    Route = "#",         Icon = Icons.Material.Filled.Info },           // TODO: placeholder until About ships
    ];

    public static readonly List<PageRoute> AllPages = 
        new List<PageRoute>
        {
            new() { Name = "Home", Route = "/", Icon = Icons.Material.Filled.Home }
        }.Concat(MenuPages).ToList();
}