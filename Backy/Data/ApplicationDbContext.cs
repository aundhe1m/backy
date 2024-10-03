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
        public DbSet<PoolDrive> PoolDrives { get; set; }
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
                .HasForeignKey(d => d.PoolGroupGuid)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<PoolDrive>()
                .HasOne(d => d.PoolGroup)
                .WithMany(pg => pg.Drives)
                .HasForeignKey(d => d.PoolGroupGuid)
                .HasPrincipalKey(pg => pg.PoolGroupGuid)
                .OnDelete(DeleteBehavior.Cascade);

            base.OnModelCreating(modelBuilder);
        }
    }
}
