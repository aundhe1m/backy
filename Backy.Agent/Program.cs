using Backy.Agent.Endpoints;
using Backy.Agent.Middleware;
using Backy.Agent.Models;
using Backy.Agent.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

// Check if running with elevated permissions (root/sudo)
if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
{
    // Check effective user ID (0 = root)
    int uid = GetEffectiveUserId();
    if (uid != 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("ERROR: This application requires elevated permissions (root/sudo).");
        Console.WriteLine("Please restart with 'sudo dotnet run' or as root user.");
        Console.ResetColor();
        Environment.Exit(1);
    }
}

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to container
builder.Services.AddEndpointsApiExplorer();

// Add Swagger with configuration for API documentation
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Backy Agent API",
        Version = "v1",
        Description = "API for managing drives, pools and system operations in the Backy Agent"
    });
    
    // Add API key authentication to Swagger UI
    options.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "X-Api-Key",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Description = "API key needed to access the endpoints"
    });
    
    var securityRequirement = new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            new string[] {}
        }
    };
    
    options.AddSecurityRequirement(securityRequirement);
});

// Configure JSON serialization
builder.Services.Configure<JsonSerializerOptions>(options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.PropertyNameCaseInsensitive = true;
});

// Bind AgentSettings from configuration
builder.Services.Configure<AgentSettings>(builder.Configuration.GetSection("AgentSettings"));

// Register services
builder.Services.AddSingleton<ISystemCommandService, SystemCommandService>();
builder.Services.AddScoped<IDriveService, DriveService>();
builder.Services.AddScoped<IPoolService, PoolService>();

// Register hosted service for pool metadata validation at startup
builder.Services.AddHostedService<PoolMetadataValidationService>();

// Configure CORS if needed
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Configure port from settings
var port = builder.Configuration.GetSection("AgentSettings").GetValue<int>("ApiPort");
if (port > 0)
{
    builder.WebHost.UseUrls($"http://*:{port}");
}

// Build the app
var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Backy Agent API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "Backy Agent API";
});

app.UseHttpsRedirection();
app.UseCors();

// Add API key authentication middleware
app.UseApiKeyAuthentication();

// Configure API endpoints
app.MapDriveEndpoints();
app.MapPoolEndpoints();
app.MapSystemEndpoints();

// Add a status endpoint to check if agent is running
app.MapGet("/api/v1/status", () =>
{
    return Results.Ok(new 
    { 
        Status = "Running",
        Version = "1.0.0",
        Timestamp = DateTime.UtcNow
    });
}).WithTags("System").WithName("GetStatus");

// Start the application
app.Run();

// Method to get the effective user ID on Unix systems
static int GetEffectiveUserId()
{
    try
    {
        // Run 'id -u' to get current user id
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "id",
                Arguments = "-u",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        process.Start();
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        
        if (int.TryParse(output, out int uid))
        {
            return uid;
        }
    }
    catch
    {
        // If we can't determine the UID, assume we're not root
    }
    
    return -1; // Not root
}
