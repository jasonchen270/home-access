using Microsoft.AspNetCore.Identity;

namespace HomeAccess.Api.Models;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";

    public ICollection<UserDeviceAccess> DeviceAccess { get; set; } = new List<UserDeviceAccess>();
}

public class Device
{
    public int Id { get; set; }
    public string Name { get; set; } = "";          // "Front Door", "Garage"
    public string MqttTopic { get; set; } = "";     // e.g. "home/door/front"
    public bool IsOnline { get; set; }              // updated when the Pi publishes a heartbeat
    public DateTimeOffset? LastSeenAt { get; set; }
}

// Audit log: every unlock attempt, success, failure, or physical-button press.
public class EntryEvent
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    public string? UserId { get; set; }             // null if the event came from the physical button
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

// Join table between user and device, with a per-device access schedule.
public class UserDeviceAccess
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;

    public int DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    // JSON schedule, e.g. {"mon":[["07:00","22:00"]], "tue":[["07:00","22:00"]], ...}
    public string ScheduleJson { get; set; } = "{}";
}
