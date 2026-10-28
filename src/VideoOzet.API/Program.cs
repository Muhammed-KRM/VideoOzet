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
if (isStandalone && !VideoOzet.API.Security.HardwareLockGuard.EnsureHardwareAuthorized(args))
{
    return;
}
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
                OpenBrowserCrossPlatform(targetUrl);
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

        // Mevcut veritabanı kayıtlarındaki Markdown format farklarını (köşeli parantez kalıntıları, birleşik prompter paragrafları, vb.) idempotent olarak normalize et
        bool dbChanged = false;
        var orphanId = Guid.Parse("114b9821-bc85-4768-b9c4-8a93893fc0cf");
        var orphanFailed = db.ContentRequests.Where(r => r.Id == orphanId).ToList();
        if (orphanFailed.Count > 0)
        {
            db.ContentRequests.RemoveRange(orphanFailed);
            dbChanged = true;
        }

        var allVersions = db.ContentVersions.ToList();
        foreach (var gc in db.GeneratedContents.ToList())
        {
            var normOzet = VideoOzet.Business.Infrastructure.AI.ClaudeSynthesisProvider.NormalizeMarkdownOutput(gc.ArastirmaOzeti ?? string.Empty, isVideoPlan: false);
            var normPlan = VideoOzet.Business.Infrastructure.AI.ClaudeSynthesisProvider.NormalizeMarkdownOutput(gc.VideoPlani ?? string.Empty, isVideoPlan: true);
            if (normOzet != gc.ArastirmaOzeti || normPlan != gc.VideoPlani)
            {
                gc.ArastirmaOzeti = normOzet;
                gc.VideoPlani = normPlan;
                dbChanged = true;
            }

            var reqVersions = allVersions.Where(v => v.ContentRequestId == gc.ContentRequestId).ToList();
            foreach (var ver in reqVersions)
            {
                if (ver.VersiyonNo == 1 && reqVersions.Count > 1 && normOzet.Length > (ver.ArastirmaOzeti?.Length ?? 0))
                {
                    ver.ArastirmaOzeti = normOzet;
                    ver.VideoPlani = normPlan;
                    dbChanged = true;
                }
                else
                {
                    var vOzet = VideoOzet.Business.Infrastructure.AI.ClaudeSynthesisProvider.NormalizeMarkdownOutput(ver.ArastirmaOzeti ?? string.Empty, isVideoPlan: false);
                    var vPlan = VideoOzet.Business.Infrastructure.AI.ClaudeSynthesisProvider.NormalizeMarkdownOutput(ver.VideoPlani ?? string.Empty, isVideoPlan: true);
                    if (vOzet != ver.ArastirmaOzeti || vPlan != ver.VideoPlani)
                    {
                        ver.ArastirmaOzeti = vOzet;
                        ver.VideoPlani = vPlan;
                        dbChanged = true;
                    }
                }
            }
        }

        foreach (var rev in db.BolumRevizyonlari.ToList())
        {
            var rOzet = VideoOzet.Business.Infrastructure.AI.ClaudeSynthesisProvider.NormalizeMarkdownOutput(rev.ArastirmaOzeti ?? string.Empty, isVideoPlan: false);
            var rPlan = VideoOzet.Business.Infrastructure.AI.ClaudeSynthesisProvider.NormalizeMarkdownOutput(rev.VideoPlani ?? string.Empty, isVideoPlan: true);
            if (rOzet != rev.ArastirmaOzeti || rPlan != rev.VideoPlani)
            {
                rev.ArastirmaOzeti = rOzet;
                rev.VideoPlani = rPlan;
                dbChanged = true;
            }
        }

        if (dbChanged)
        {
            db.SaveChanges();
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
        var url = app.Urls.FirstOrDefault() ?? builder.Configuration["Urls"] ?? "http://localhost:5050";
        OpenBrowserCrossPlatform(url);
    });
}

app.Run();

static void OpenBrowserCrossPlatform(string url)
{
    try
    {
        if (OperatingSystem.IsMacOS())
        {
            System.Diagnostics.Process.Start("open", url);
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", url);
        }
        else
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }
    catch { }
}

public partial class Program { }
