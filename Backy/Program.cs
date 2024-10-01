using Backy.Data;
using Backy.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Backy;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorPages();

        // Configure MySQL Database Context
        var connectionString =
            builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found."
            );
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString)
        );

        // Add Data Protection Services
        builder
            .Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo("/mnt/backy/backy-keys/")); // Ensure this directory exists and is accessible

        // Register the hosted service
        builder.Services.AddHostedService<StorageStatusService>();
        builder.Services.AddHostedService<FileIndexingService>();
        builder.Services.AddSingleton<IIndexingQueue, IndexingQueue>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }
        app.UseDefaultFiles();

        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthorization();

        app.MapRazorPages();

        app.Run();
    }
}
