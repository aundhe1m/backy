using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace Backy.Data
{
    public class ApplicationDbContextFactory : 
        IDbContextFactory<ApplicationDbContext>,
        IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext>? _options;

        // Parameterless constructor for design-time use
        public ApplicationDbContextFactory()
        {
            // This constructor is used by EF Core design-time tools
            // The _options will be null, but that's okay because CreateDbContext(string[])
            // doesn't use _options.
        }

        // Constructor for runtime use via DI
        public ApplicationDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        {
            _options = options;
        }

        // For runtime use
        public ApplicationDbContext CreateDbContext()
        {
            // If _options is null, we're being called outside of the DI container
            // In that case, create options using the same logic as in CreateDbContext(string[])
            if (_options == null)
            {
                return CreateDbContext(Array.Empty<string>());
            }
            
            return new ApplicationDbContext(_options);
        }

        // For design-time use (migrations, etc.)
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            // Build configuration
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            // Get connection string
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
