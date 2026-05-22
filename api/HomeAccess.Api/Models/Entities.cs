// =============================================================================
// Entities.cs holds the domain model. These C# classes become SQL tables via EF Core.
//
// LEARNING NOTES on EF Core conventions:
//   - A property named "Id" or "<ClassName>Id" automatically becomes the primary key.
//   - A property of a reference-type class becomes a navigation property; EF infers
//     the foreign key by name (e.g. `Device Device` + `int DeviceId` → FK).
//   - ICollection<T> on the "one" side of a one-to-many means "this entity has many T".
//   - You DON'T need [Table] / [Column] attributes unless you're overriding conventions.
// =============================================================================

using Microsoft.AspNetCore.Identity;

namespace HomeAccess.Api.Models;

// AppUser inherits from IdentityUser, which already has Id, UserName, Email,
// PasswordHash, etc. We just add app-specific fields.
public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";

    // Navigation: a user can have access to many devices (with per-device schedules).
    public ICollection<UserDeviceAccess> DeviceAccess { get; set; } = new List<UserDeviceAccess>();
}

public class Device
{
    public int Id { get; set; }
    public string Name { get; set; } = "";          // "Front Door", "Garage"
    public string MqttTopic { get; set; } = "";     // e.g. "home/door/front"
    public bool IsOnline { get; set; }              // updated when Pi publishes heartbeat
    public DateTimeOffset? LastSeenAt { get; set; } // DateTimeOffset → tz-safe (see insight below)
}

// EntryEvent = the audit log. Every unlock attempt, success, failure, or
// physical-button press lands here. This is the "view entry logs" feature.
public class EntryEvent
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public Device Device { get; set; } = null!;     // null! = "I promise EF will populate this"

    public string? UserId { get; set; }             // null if event came from physical button
    public AppUser? User { get; set; }

    public EntryEventType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Note { get; set; }
}

public enum EntryEventType
{
    UnlockRequested = 1,
    UnlockGranted = 2,
    UnlockDenied = 3,
    PhysicalEntry = 4,
    DeviceOnline = 5,
    DeviceOffline = 6,
}

// UserDeviceAccess = the JOIN TABLE between User and Device, plus a schedule.
// This is the "per-user schedules" + "household members only see allowed devices" feature.
public class UserDeviceAccess
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;

    public int DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    // Stored as JSON for simplicity. A richer model would be a separate Schedule entity
    // with day-of-week + time-range rows, but JSON keeps the table count down for learning.
    // Example: {"mon":[["07:00","22:00"]], "tue":[["07:00","22:00"]], ...}
    public string ScheduleJson { get; set; } = "{}";
}
