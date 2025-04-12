using Backy.Agent.Models;

namespace Backy.Agent.Middleware;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;
    private readonly string _apiKey;
    private readonly bool _authDisabled;

    public ApiKeyAuthMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyAuthMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _apiKey = configuration["AgentSettings:ApiKey"] ?? "";
        _authDisabled = configuration.GetValue<bool>("AgentSettings:DisableApiAuthentication");
        
        if (_authDisabled)
        {
            _logger.LogWarning("API authentication is disabled. This is not recommended for production environments.");
        }
        else if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("API key is not configured. Authentication is disabled.");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for Swagger endpoints
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }
        
        // Skip authentication if disabled
        if (_authDisabled)
        {
            await _next(context);
            return;
        }
        
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey))
        {
            _logger.LogWarning("API key was not provided. Request from {IpAddress}", 
                context.Connection.RemoteIpAddress);
            
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "UNAUTHORIZED",
                    message = "API key is required"
                }
            });
            
            return;
        }

        if (!string.IsNullOrEmpty(_apiKey) && !string.Equals(extractedApiKey, _apiKey))
        {
            _logger.LogWarning("Invalid API key provided. Request from {IpAddress}",
                context.Connection.RemoteIpAddress);
            
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "UNAUTHORIZED",
                    message = "Invalid API key"
                }
            });
            
            return;
        }

        await _next(context);
    }
}

// Extension method for easy registration
public static class ApiKeyAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuthentication(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ApiKeyAuthMiddleware>();
    }
}