using DeepDrftWeb;
using DeepDrftWeb.Client.Services;
using MudBlazor.Services;
using DeepDrftWeb.Components;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Add AudioInteropService for both server and client rendering
builder.Services.AddScoped<AudioInteropService>();

var baseUrl = Startup.GetKestrelUrl(builder);
var contentApiUrl = builder.Configuration["ApiUrls:ContentApi"] ?? "https://localhost:7001";

Startup.ConfigureDomainServices(builder);

DeepDrftWeb.Client.Startup.ConfigureApiHttpClient(builder.Services, baseUrl);
DeepDrftWeb.Client.Startup.ConfigureCommonServices(builder.Services, contentApiUrl);
DeepDrftWeb.Client.Startup.ConfigureDomainServices(builder.Services);

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
