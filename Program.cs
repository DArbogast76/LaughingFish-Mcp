using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.Exporter;
using LaughingFish.Mcp.Clients;
using LaughingFish.Mcp.Configuration;
using LaughingFish.Mcp.Location;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.Configure<McpOptions>(builder.Configuration);
builder.Services.AddHttpClient<ISunriseSunsetApiClient, SunriseSunsetApiClient>();
builder.Services.AddHttpClient<IWeatherApiClient, WeatherApiClient>();
builder.Services.AddHttpClient<IWaterTempApiClient, WaterTempApiClient>();
builder.Services.AddHttpClient<ILocationResolver, AzureMapsLocationResolver>();

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var telemetry = builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults();

var appInsightsConnection =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["AzureMonitor:ConnectionString"];

if (!string.IsNullOrWhiteSpace(appInsightsConnection))
{
    telemetry.UseAzureMonitorExporter();
}

builder.Logging.AddConsole();

builder.Build().Run();
