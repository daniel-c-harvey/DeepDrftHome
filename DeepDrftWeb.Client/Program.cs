using DeepDrftWeb.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

Console.WriteLine(builder.HostEnvironment.BaseAddress);

var contentApiUrl = builder.Configuration["ApiUrls:ContentApi"] ?? "https://localhost:7001";

builder.Services.AddMudServices();

Startup.ConfigureApiHttpClient(builder.Services, builder.HostEnvironment.BaseAddress);
Startup.ConfigureContentServices(builder.Services, contentApiUrl);
Startup.ConfigureDomainServices(builder.Services);

var app = builder.Build();

await app.RunAsync();
