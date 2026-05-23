using HomeAccess.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HomeAccess.Api.Data;

// IdentityDbContext so Identity's tables are created alongside the app's.
public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<EntryEvent> EntryEvents => Set<EntryEvent>();
    public DbSet<UserDeviceAccess> UserDeviceAccess => Set<UserDeviceAccess>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);   // required: configures Identity tables

        // SQLite has no native date type and can't ORDER BY / compare a DateTimeOffset
        // column, which would break the entry-log query. Store it as UTC ms (a 64-bit
        // int) so ordering and range filters run server-side. Harmless on SQL Server,
        // which supports DateTimeOffset natively.
        var dtoConverter = new ValueConverter<DateTimeOffset, long>(
            v => v.ToUniversalTime().ToUnixTimeMilliseconds(),
            v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        var nullableDtoConverter = new ValueConverter<DateTimeOffset?, long?>(
            v => v.HasValue ? v.Value.ToUniversalTime().ToUnixTimeMilliseconds() : (long?)null,
            v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : (DateTimeOffset?)null);

        b.Entity<EntryEvent>().Property(e => e.OccurredAt).HasConversion(dtoConverter);
        b.Entity<Device>().Property(d => d.LastSeenAt).HasConversion(nullableDtoConverter);

        // The entry-log query filters by device + time.
        b.Entity<EntryEvent>()
            .HasIndex(e => new { e.DeviceId, e.OccurredAt });

        // One access row per user/device.
        b.Entity<UserDeviceAccess>()
            .HasIndex(a => new { a.UserId, a.DeviceId })
            .IsUnique();
    }
}
