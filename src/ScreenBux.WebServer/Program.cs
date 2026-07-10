using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ScreenBux.Data;
using ScreenBux.Data.Entities;
using ScreenBux.WebServer.Hubs;
using ScreenBux.WebServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EF Core (SQL Server for both local dev and Azure SQL prod).
var connectionString = builder.Configuration.GetConnectionString("AppDb")
    ?? throw new InvalidOperationException("Connection string 'AppDb' is not configured.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// ASP.NET Core Identity as the user store (JWT is issued separately, no cookies).
builder.Services
    .AddIdentityCore<Account>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<AppDbContext>();

// JWT bearer authentication for all clients (Blazor, Service, device).
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection["SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");
var tokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidIssuer = jwtSection["Issuer"],
    ValidateAudience = true,
    ValidAudience = jwtSection["Audience"],
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
    ValidateLifetime = true,
    ClockSkew = TimeSpan.FromMinutes(1)
};

builder.Services.AddSingleton(tokenValidationParameters);
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<IPolicyStore, EfPolicyStore>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = tokenValidationParameters;

        // Allow SignalR clients to send the token via the query string, since browsers
        // cannot set Authorization headers on the WebSocket handshake.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/monitoringHub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Add SignalR
builder.Services.AddSignalR();

// Add CORS for web client
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebClient", policy =>
    {
        policy.WithOrigins(
                  "https://localhost:7123", "http://localhost:5239",   // WebClient Kestrel (dotnet run)
                  "https://localhost:44331", "http://localhost:15426")  // WebClient IIS Express
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        // Use an absolute path for the definition as a small robustness measure so the
        // URL never depends on the current browser path (redirects, trailing slash, etc.).
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ScreenBux.WebServer v1");
    });
}

app.UseHttpsRedirection();

app.UseCors("AllowWebClient");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Map SignalR hub
app.MapHub<MonitoringHub>("/monitoringHub");

app.Run();
