using Backy.Models;
using Microsoft.EntityFrameworkCore;

namespace Backy.Data
{
    public class ApplicationDbContext : DbContext
    {
        public DbSet<RemoteScan> RemoteScans { get; set; }
        public DbSet<IndexSchedule> IndexSchedules { get; set; }
        public DbSet<FileEntry> Files { get; set; }
        public DbSet<StorageContent> StorageContents { get; set; }

        public DbSet<PoolGroup> PoolGroups { get; set; }
        public DbSet<Drive> Drives { get; set; }
        public DbSet<ProtectedDrive> ProtectedDrives { get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // Configure model relationships and constraints
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<FileEntry>()
                .HasIndex(f => new { f.RemoteScanId, f.FullPath })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            modelBuilder
                .Entity<PoolGroup>()
                .HasMany(pg => pg.Drives)
                .WithOne(d => d.PoolGroup)
                .HasForeignKey(d => d.PoolGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<Drive>()
                .HasMany(d => d.Partitions)
                .WithOne(p => p.Drive)
                .HasForeignKey(p => p.DriveId)
                .OnDelete(DeleteBehavior.Cascade);

            base.OnModelCreating(modelBuilder);
        }
    }
}
