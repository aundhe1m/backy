using Backy.Models;
using Microsoft.EntityFrameworkCore;

namespace Backy.Data
{
    public class ApplicationDbContext : DbContext
    {
        public DbSet<PoolGroup> PoolGroups { get; set; }
        public DbSet<PoolDrive> PoolDrives { get; set; }
        public DbSet<ProtectedDrive> ProtectedDrives { get; set; }

        public DbSet<RemoteConnection> RemoteConnections { get; set; }
        public DbSet<RemoteScanSchedule> RemoteScanSchedules { get; set; }
        public DbSet<RemoteFile> RemoteFiles { get; set; }
        public DbSet<RemoteFilter> RemoteFilters { get; set; }


        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // Configure model relationships and constraints
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            modelBuilder.Entity<PoolDrive>()
                .HasIndex(d => d.Id)
                .IsUnique();

            modelBuilder.Entity<PoolDrive>()
                .HasIndex(d => d.DriveGuid)
                .IsUnique();

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

            modelBuilder.Entity<RemoteConnection>()
                .HasKey(rc => rc.RemoteConnectionId);

            modelBuilder.Entity<RemoteScanSchedule>()
                .HasKey(rss => rss.Id);

            modelBuilder.Entity<RemoteScanSchedule>()
                .HasOne(rss => rss.RemoteConnection)
                .WithMany(rc => rc.ScanSchedules)
                .HasForeignKey(rss => rss.RemoteConnectionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RemoteFile>()
                .HasKey(rf => rf.Id);

            modelBuilder.Entity<RemoteFile>()
                .HasOne(rf => rf.RemoteConnection)
                .WithMany(rc => rc.RemoteFiles)
                .HasForeignKey(rf => rf.RemoteConnectionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RemoteFilter>()
                .HasKey(rf => rf.Id);

            modelBuilder.Entity<RemoteFilter>()
                .HasOne(rf => rf.RemoteConnection)
                .WithMany(rc => rc.Filters)
                .HasForeignKey(rf => rf.RemoteConnectionId)
                .OnDelete(DeleteBehavior.Cascade);

            base.OnModelCreating(modelBuilder);
        }
    }
}
