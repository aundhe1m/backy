using Backy.Components;
using Backy.Data;
using Backy.Models;
using Backy.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using System;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Backy.Configuration;
using System.Text.RegularExpressions;

// Create a builder for the web application
var builder = WebApplication.CreateBuilder(args);

// Add this line after the builder is created
builder.ConfigureEnvironmentVariableMapping();

// ---------------------------
// Service Configuration
// ---------------------------

// Add Razor Pages support
builder.Services.AddRazorPages();

// Add Razor Components with interactive server components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Server-Side Blazor services
builder.Services.AddServerSideBlazor();

// Add Blazor Bootstrap for styling and components
builder.Services.AddBlazorBootstrap();

// Configure Entity Framework with PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Singleton services are created once and reused throughout the application's lifetime
builder.Services.AddSingleton<ILoadingService, LoadingService>();
builder.Services.AddSingleton<ThemeService, ThemeService>();
builder.Services.AddSingleton<ITimeZoneService, TimeZoneService>();
builder.Services.AddSingleton<ConnectionEventService>();

// Configure Backy Agent settings
builder.Services.Configure<BackyAgentConfig>(builder.Configuration.GetSection("BackyAgent"));

// Configure HttpClient for Backy Agent with resilience policies
builder.Services.AddHttpClient<IBackyAgentClient, BackyAgentClient>((serviceProvider, client) => 
{
    // Access IConfiguration from service provider to pass it to the client
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    // BackyAgentClient constructor now receives IConfiguration from the factory
})
    .AddPolicyHandler((services, request) => 
    {
        var config = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackyAgentConfig>>().Value;
        
        return HttpPolicyExtensions
            .HandleTransientHttpError() // HttpRequestException, 5XX and 408 status codes
            .Or<TimeoutRejectedException>() // Thrown by Polly's TimeoutPolicy
            .OrResult(response => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests) // 429
            .WaitAndRetryAsync(config.MaxRetryAttempts, retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) // Exponential backoff
            );
    })
    .AddPolicyHandler((services, request) => 
    {
        var config = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackyAgentConfig>>().Value;
        
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(
                config.CircuitBreakerThreshold, 
                TimeSpan.FromSeconds(config.CircuitBreakerDurationSeconds)
            );
    })
    .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(60)));

// Register the Backy Agent drive service instead of the local one
builder.Services.AddScoped<IDriveService, AgentDriveService>();

// Other scoped services are created once per client request (connection)
builder.Services.AddScoped<IRemoteConnectionService, RemoteConnectionService>();

// Register the background service for monitoring schedules
builder.Services.AddHostedService<ScheduleMonitorService>();

// Register the background service for refreshing drive data
builder.Services.AddHostedService<DriveRefreshService>();

// Add data protection services for safeguarding data
builder.Services.AddDataProtection();

// Register ConfigurationPrinter
builder.Services.AddSingleton<ConfigurationPrinter>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<BackyAgentHealthCheck>("backy_agent_connection");

// Build the web application
var app = builder.Build();

// ---------------------------
// HTTP Request Pipeline Configuration
// ---------------------------

// Configure error handling and HTTP Strict Transport Security (HSTS) for production
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. Consider changing this for production scenarios.
    app.UseHsts();
}

// Enable serving static files (e.g., CSS, JS, images)
app.UseStaticFiles();

// Enable anti-forgery protection to prevent CSRF attacks
app.UseAntiforgery();

// Map Razor Components with interactive server-side rendering
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add a health endpoint for container orchestration
app.MapGet("/health", () => "Healthy");

// Map health endpoint with detailed output
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        };
        
        await context.Response.WriteAsJsonAsync(response);
    }
});

// Apply any pending database migrations and ensure the database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// Print essential configuration at startup
app.Services.GetRequiredService<ConfigurationPrinter>().PrintAllConfiguration();

// Run the web application
app.Run();
