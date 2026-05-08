using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Portlite.Api.BackgroundJobs;
using Portlite.Api.Infrastructure;
using Portlite.Api.Services;
using Portlite.Infrastructure;
using Portlite.Infrastructure.Ai;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Portlite API", Version = "v1" });
    c.UseInlineDefinitionsForEnums();
});

builder.Services.AddPortliteInfrastructure(builder.Configuration);

builder.Services.AddScoped<SubPortfolioService>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<TradeService>();
builder.Services.AddScoped<CashTransactionService>();
builder.Services.AddScoped<PositionCalculator>();
builder.Services.AddScoped<PortfolioSnapshotService>();
builder.Services.AddScoped<KpiService>();
builder.Services.AddScoped<WatchlistService>();

builder.Services.Configure<AzureFoundryOptions>(
    builder.Configuration.GetSection(AzureFoundryOptions.SectionName));
builder.Services.AddSingleton<IAiAnalysisClient, AzureFoundryAnalysisClient>();
builder.Services.AddScoped<PortfolioAnalysisService>();
builder.Services.AddScoped<NewsService>();

builder.Services.Configure<SnapshotJobOptions>(builder.Configuration.GetSection(SnapshotJobOptions.SectionName));
builder.Services.AddSingleton<DailySnapshotHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DailySnapshotHostedService>());

const string BlazorCorsPolicy = "BlazorClient";
builder.Services.AddCors(options =>
{
    options.AddPolicy(BlazorCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5012", "https://localhost:7012")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.Configure<ApiBehaviorOptions>(opts =>
{
    opts.SuppressMapClientErrors = true;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();

app.UseCors(BlazorCorsPolicy);

app.UseAuthorization();
app.MapControllers();

app.Run();
