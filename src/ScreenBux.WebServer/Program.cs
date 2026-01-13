using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ScreenBux.WebServer.Data;
using ScreenBux.WebServer.Hubs;
using ScreenBux.WebServer.Models;
using ScreenBux.WebServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebClient", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=screenbux.db";
    options.UseSqlite(connectionString);
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddDataProtection();
builder.Services.AddMemoryCache();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ScreenBux",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ScreenBuxDevices",
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"] ?? "ChangeThisSigningKey"))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/monitoringHub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("UserOnly", policy =>
    {
        policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme);
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy("DeviceOnly", policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireClaim("role", "device");
    });

    options.AddPolicy("UserOrDevice", policy =>
    {
        policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddScoped<IDeviceSecretProtector, DeviceSecretProtector>();
builder.Services.AddSingleton<IDeviceNonceStore, DeviceNonceStore>();
builder.Services.AddSingleton<IDeviceTokenService, DeviceTokenService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowWebClient");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<MonitoringHub>("/monitoringHub");

app.Run();
