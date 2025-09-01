using DeepDrftWeb;
using MudBlazor.Services;
using DeepDrftWeb.Components;
using DeepDrftWeb.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

Startup.ConfigureDomainServices(builder);

var hostUrl = builder.Configuration.GetValue<string>("ASPNETCORE_URLS")?.Split(';').First() ?? throw new Exception("ASPNETCORE_URLS undefined");
DeepDrftWeb.Client.Startup.ConfigureDomainServices(builder.Services, hostUrl);

builder.Services.AddControllers();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DeepDrftWeb.Client._Imports).Assembly);

app.Run();
