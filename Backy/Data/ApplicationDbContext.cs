using Backy.Models;
using Microsoft.EntityFrameworkCore;

namespace Backy.Data
{
    public class ApplicationDbContext : DbContext
    {
        public DbSet<RemoteStorage> RemoteStorages { get; set; }
        public DbSet<FileEntry> Files { get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Configure model relationships and constraints
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FileEntry>()
                .HasIndex(f => new { f.RemoteStorageId, f.FullPath })
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }
    }
}
