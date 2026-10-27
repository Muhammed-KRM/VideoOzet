using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using VideoOzet.Business;
using VideoOzet.Data;
using VideoOzet.API.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Load root .env file if present
var searchDir = new DirectoryInfo(Directory.GetCurrentDirectory());
while (searchDir != null)
{
    var envPath = Path.Combine(searchDir.FullName, ".env");
    if (File.Exists(envPath))
    {
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
            var parts = trimmed.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var val = parts[1].Trim();
                Environment.SetEnvironmentVariable(key, val);
                builder.Configuration[key] = val;
            }
        }
        break;
    }
    searchDir = searchDir.Parent;
}

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

// Configure Kestrel limits
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = null; // Unlimited for large files
});

// Configure FormOptions for multipart body limit
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

// Register Data and Business layers
var isStandalone = builder.Configuration.GetValue<bool>("StandaloneMode");
if (isStandalone && builder.Configuration.GetValue<bool>("AutoOpenBrowser"))
{
    var targetUrl = builder.Configuration["Urls"] ?? "http://localhost:5050";
    if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            if (tcp.ConnectAsync("127.0.0.1", uri.Port).Wait(300))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = targetUrl,
                    UseShellExecute = true
                });
                return;
            }
        }
        catch { }
    }
}

var connStr = isStandalone
    ? $"Data Source={Path.Combine(AppContext.BaseDirectory, "videoozet.db")}"
    : builder.Configuration.GetConnectionString("DefaultConnection")!;

builder.Services.AddDataLayer(connStr);
builder.Services.AddBusinessLayer(builder.Configuration, x => 
{
    x.AddConsumer<VideoOzet.API.Consumers.PipelineProgressConsumer>();

    if (isStandalone)
    {
        x.AddConsumer<VideoOzet.Worker.Consumers.ExtractTranscriptConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.SummarizeVideoConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.IndexSummaryConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.GenerateContentConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.QualityCheckConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.ExtractDokumanTextConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.IndexDokumanConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.TopicAnalysisConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.TopicAnalysisCompletedConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.SeriesPlanConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.SeriesVideoGenerationConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.SeriesVideoGeneratedConsumer>();
        x.AddConsumer<VideoOzet.Worker.Consumers.SeriesVideoRevisionConsumer>();
    }
});

// CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Otomatik veritabanı oluşturma / migration
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<VideoOzet.Data.Context.AppDbContext>();
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            db.Database.EnsureCreated();
        }
        else
        {
            db.Database.Migrate();
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILogger<Program>>();
        logger?.LogWarning(ex, "Veritabanı migration kontrolü atlandı veya hata oluştu.");
    }
}

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowFrontend");

// Use Custom API Key Middleware for auth
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthorization();
app.MapControllers();
app.MapHub<VideoOzet.API.Hubs.PipelineHub>("/hubs/pipeline");
app.MapFallbackToFile("index.html");

if (builder.Configuration.GetValue<bool>("AutoOpenBrowser"))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var url = app.Urls.FirstOrDefault() ?? "http://localhost:5002";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    });
}

app.Run();

public partial class Program { }
