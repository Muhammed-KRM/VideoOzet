using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoOzet.Business;
using VideoOzet.Data;
using VideoOzet.API.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

// Register Data and Business layers
builder.Services.AddDataLayer(builder.Configuration.GetConnectionString("DefaultConnection")!);
builder.Services.AddBusinessLayer(builder.Configuration, x => 
{
    x.AddConsumer<VideoOzet.API.Consumers.PipelineProgressConsumer>();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Use Custom API Key Middleware for auth
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthorization();
app.MapControllers();
app.MapHub<VideoOzet.API.Hubs.PipelineHub>("/hubs/pipeline");

app.Run();

public partial class Program { }
