using ReceiptScanner.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register HttpClient for Ollama
builder.Services.AddHttpClient("Ollama");

// Register scanner providers
builder.Services.AddSingleton<IReceiptScannerProvider, AzureDocumentIntelligenceProvider>();
builder.Services.AddSingleton<IReceiptScannerProvider, OllamaVisionProvider>();

// Register main scanner service
builder.Services.AddSingleton<IReceiptScannerService, ReceiptScannerService>();

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Receipt Scanner API v1");
        options.RoutePrefix = string.Empty; // Swagger at root
    });
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// Log available providers on startup
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var scannerService = app.Services.GetRequiredService<IReceiptScannerService>();
var providers = scannerService.GetAvailableProviders().ToList();

logger.LogInformation("Receipt Scanner API started");
logger.LogInformation("Available providers: {Providers}", string.Join(", ", providers));

if (providers.Count == 0)
{
    logger.LogWarning("No scanner providers are configured! Please check appsettings.json");
}

app.Run();
