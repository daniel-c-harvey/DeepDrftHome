using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace DeepDrftWeb.Client.Controls;

public partial class AppNavLink : ComponentBase
{
    [Parameter] public required string Href { get; set; }
    [Parameter] public NavLinkMatch? Match { get; set; }
    [Parameter] public string? Icon { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
}