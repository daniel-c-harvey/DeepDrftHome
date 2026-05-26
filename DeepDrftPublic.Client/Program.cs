using DeepDrftPublic.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

// Both named clients point at this host's own origin. DeepDrftPublic proxies the
// public track routes to DeepDrftAPI internally so the browser never makes a
// cross-origin request.
Startup.ConfigureApiHttpClient(builder.Services, builder.HostEnvironment.BaseAddress);
Startup.ConfigureContentServices(builder.Services, builder.HostEnvironment.BaseAddress);
Startup.ConfigureDomainServices(builder.Services);

await builder.Build().RunAsync();
