using Aspire.Hosting.DevTunnels;

var builder = DistributedApplication.CreateBuilder(args);

// The API reads its Azure endpoints and AzureAd settings from its own appsettings.json and user secrets.
// The http endpoint is not proxied, so the API itself listens on 5198: the port the tunnel forwards.
var api = builder.AddProject<Projects.api>("api", launchProfileName: "http")
    .WithEndpoint("http", endpoint => endpoint.IsProxied = false)
    .WithHttpHealthCheck("/health");

// Reuses the persistent tunnel that APIM's backend points at (https://chatbot-api-5198.euw.devtunnels.ms).
// Pin the region: without it the dev tunnels service can pick another cluster, create a second tunnel
// with the same ID there, and APIM's backend URL stops reaching the API.
// Anonymous access lets APIM reach it; the API still validates every token.
var tunnelRegion = Enum.Parse<DevTunnelRegion>(builder.Configuration["DevTunnel:Region"] ?? nameof(DevTunnelRegion.WestEurope));
builder.AddDevTunnel("tunnel", tunnelId: builder.Configuration["DevTunnel:TunnelId"] ?? "chatbot-api",
        new DevTunnelOptions { AllowAnonymous = true, Description = "Chatbot API backend for APIM", Region = tunnelRegion })
    .WithReference(api.GetEndpoint("http"), new DevTunnelPortOptions { Protocol = "http" })
    .WaitFor(api);

// A modern page to debug the web part on (the hosted workbench retires on Dec 1, 2026).
var debugPageUrl = builder.Configuration["SharePoint:DebugPageUrl"];
var spfx = builder.AddJavaScriptApp("spfx", "../frontend/spfx", "start")
    .WithNpm(install: false)
    .WithArgs("--", "--nobrowser")
    .WithHttpsEndpoint(port: 4321, isProxied: false);

if (!string.IsNullOrWhiteSpace(debugPageUrl))
{
    spfx.WithUrl($"{debugPageUrl}?debug=true&noredir=true&debugManifestsFile="
        + Uri.EscapeDataString("https://localhost:4321/temp/build/manifests.js"), "SharePoint (debug)");
}

builder.Build().Run();
