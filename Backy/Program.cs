using Backy.Components;
using Backy.Data;
using Backy.Services;
using Microsoft.EntityFrameworkCore;

// Create a builder for the web application
var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<IIndexingQueue, IndexingQueue>();

// Scoped services are created once per client request (connection)
builder.Services.AddScoped<IDriveService, DriveService>();

// Add data protection services for safeguarding data
builder.Services.AddDataProtection();

// Register hosted services that run in the background
builder.Services.AddHostedService<FileIndexingService>();
builder.Services.AddHostedService<StorageStatusService>();

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

// Apply any pending database migrations and ensure the database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// Run the web application
app.Run();
