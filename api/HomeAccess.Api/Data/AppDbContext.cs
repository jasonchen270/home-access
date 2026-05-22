// =============================================================================
// AppDbContext.cs is EF Core's central object. Think of it as a "session" with
// the database. It holds:
//   - DbSet<T> properties → one per table
//   - A change tracker that watches entities you've loaded
//   - SaveChanges() → diffs the change tracker against the loaded state and
//     emits the minimal INSERT/UPDATE/DELETE statements
//
// We inherit from IdentityDbContext<AppUser> instead of plain DbContext so that
// Identity's tables (AspNetUsers, AspNetRoles, AspNetUserRoles, etc.) get created
// alongside ours.
// =============================================================================

using HomeAccess.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HomeAccess.Api.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<EntryEvent> EntryEvents => Set<EntryEvent>();
    public DbSet<UserDeviceAccess> UserDeviceAccess => Set<UserDeviceAccess>();

    // OnModelCreating is the "fluent API", used to configure things attributes can't,
    // or to keep the entity classes clean of EF-specific decorations.
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);  // CRITICAL: sets up Identity tables. Forgetting this is a classic bug.

        // ---- DateTimeOffset on SQLite ------------------------------------------------
        // GOTCHA worth learning: SQLite has no native date/time type, so EF Core can't
        // ORDER BY / compare a DateTimeOffset column on SQLite; it throws at query time:
        // "SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY".
        // This breaks the "view entry logs" endpoint, which does OrderByDescending(OccurredAt).
        //
        // The fix is a *value converter* that maps DateTimeOffset to a type SQLite CAN sort
        // and compare: a 64-bit integer of UTC milliseconds since the Unix epoch. Integers
        // sort numerically in SQLite, so OrderBy / range filters translate to plain SQL and
        // run server-side (important, because client-side ordering would break Take()/paging).
        // We store UTC so the value is unambiguous, and reconstruct as a UTC DateTimeOffset
        // on read (callers format to local time in the UI).
        // (On SQL Server / Azure SQL this converter is unnecessary, since that provider handles
        //  DateTimeOffset natively, but applying it unconditionally keeps behavior identical
        //  across providers, the right call for a learning repo you might re-point at Azure SQL.)
        var dtoConverter = new ValueConverter<DateTimeOffset, long>(
            v => v.ToUniversalTime().ToUnixTimeMilliseconds(),                       // write: UTC ms
            v => DateTimeOffset.FromUnixTimeMilliseconds(v));                        // read:  back to UTC DTO
        var nullableDtoConverter = new ValueConverter<DateTimeOffset?, long?>(
            v => v.HasValue ? v.Value.ToUniversalTime().ToUnixTimeMilliseconds() : (long?)null,
            v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : (DateTimeOffset?)null);

        b.Entity<EntryEvent>().Property(e => e.OccurredAt).HasConversion(dtoConverter);
        b.Entity<Device>().Property(d => d.LastSeenAt).HasConversion(nullableDtoConverter);

        // Index for the "view entry logs" query: almost every read filters by device + time.
        b.Entity<EntryEvent>()
            .HasIndex(e => new { e.DeviceId, e.OccurredAt });

        // Prevent a user from having two access rows for the same device.
        b.Entity<UserDeviceAccess>()
            .HasIndex(a => new { a.UserId, a.DeviceId })
            .IsUnique();
    }
}
