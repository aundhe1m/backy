using Backy.Components;
using Backy.Data;
using Backy.Services;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddServerSideBlazor();
builder.Services.AddBlazorBootstrap();

// Configure DbContext with PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register LoadingService
builder.Services.AddSingleton<ILoadingService, LoadingService>();

// Register DriveService
builder.Services.AddScoped<IDriveService, DriveService>();

// Add Data Protection
builder.Services.AddDataProtection();

// Register Application Services
builder.Services.AddSingleton<ThemeService, ThemeService>();
builder.Services.AddSingleton<IIndexingQueue, IndexingQueue>();
builder.Services.AddHostedService<FileIndexingService>();
builder.Services.AddHostedService<StorageStatusService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios.
    app.UseHsts();
}

// app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// app.UseRouting();

// app.MapBlazorHub();

// Apply pending migrations and ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();
