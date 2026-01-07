using ScreenBux.WebServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR
builder.Services.AddSignalR();

// Add CORS for web client
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebClient", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173") // Blazor default ports
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
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowWebClient");

app.UseAuthorization();

app.MapControllers();

// Map SignalR hub
app.MapHub<MonitoringHub>("/monitoringHub");

app.Run();
