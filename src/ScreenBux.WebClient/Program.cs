using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using ScreenBux.WebClient.Components;
using ScreenBux.WebClient.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Authentication state (per-circuit JWT held in TokenProvider).
builder.Services.AddScoped<TokenProvider>();
builder.Services.AddScoped<TokenAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<TokenAuthenticationStateProvider>());
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// Provide a default challenge scheme so ASP.NET Core's HTTP middleware can redirect
// unauthenticated requests to /login rather than crashing. Actual Blazor interactive
// auth is handled by TokenAuthenticationStateProvider + AuthorizeRouteView.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => options.LoginPath = "/login");

// Handler that attaches the bearer token to outgoing API requests.
builder.Services.AddScoped<BearerTokenHandler>();

static Uri ResolveApiBaseUri(IServiceProvider serviceProvider)
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["PolicyApiBaseUrl"] ?? "https://localhost:44323";
    return new Uri(baseUrl.TrimEnd('/') + "/");
}

// Register MonitoringService
builder.Services.AddScoped<MonitoringService>();
builder.Services.AddHttpClient<PolicyApiService>((serviceProvider, client) =>
{
    client.BaseAddress = ResolveApiBaseUri(serviceProvider);
}).AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddHttpClient<AuthApiService>((serviceProvider, client) =>
{
    client.BaseAddress = ResolveApiBaseUri(serviceProvider);
}).AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddHttpClient<DevicesApiService>((serviceProvider, client) =>
{
    client.BaseAddress = ResolveApiBaseUri(serviceProvider);
}).AddHttpMessageHandler<BearerTokenHandler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
