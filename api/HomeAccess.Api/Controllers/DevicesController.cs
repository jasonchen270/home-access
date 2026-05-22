// =============================================================================
// DevicesController.cs lists devices the current user is allowed to see,
// trigger unlocks, view entry events. This is where the SECURITY LOGIC lives:
//
//   1. List endpoint filters by UserDeviceAccess (members can't see all devices)
//   2. Unlock endpoint checks the schedule before publishing the MQTT command
//   3. Admin endpoints are gated with [Authorize(Roles = "Admin")]
//
// SECURITY MODEL (this is the home-access dashboard logic you wanted to learn):
//   - Authentication answers "who are you?" and is done by the cookie middleware.
//   - Authorization answers "what can you do?" and is done in 3 layers:
//       a) Role check on the endpoint itself  ([Authorize(Roles=...)])
//       b) Row-level filter in the LINQ query (we only return devices in the join table)
//       c) Schedule check at unlock time (member's access window)
//   - Defense in depth: even if (a) is bypassed, (b) prevents data leakage, and
//     (c) prevents physical-world action. NEVER rely on a single check.
// =============================================================================

using System.Text.Json;
using HomeAccess.Api.Data;
using HomeAccess.Api.Models;
using HomeAccess.Api.Mqtt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeAccess.Api.Controllers;

[ApiController]
[Authorize]                                    // entire controller requires auth
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _users;
    private readonly MqttBus _mqtt;

    public DevicesController(AppDbContext db, UserManager<AppUser> users, MqttBus mqtt)
    {
        _db = db; _users = users; _mqtt = mqtt;
    }

    // GET /api/devices lists devices visible to the current user.
    // Admins see all; members see only those in UserDeviceAccess.
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var user = await _users.GetUserAsync(User);
        var isAdmin = await _users.IsInRoleAsync(user!, SeedData.AdminRole);

        // LINQ join: translates to SQL `INNER JOIN`. EF Core converts this entire
        // expression tree into one parameterized query; nothing runs until ToListAsync().
        IQueryable<Device> q = isAdmin
            ? _db.Devices
            : from d in _db.Devices
              join a in _db.UserDeviceAccess on d.Id equals a.DeviceId
              where a.UserId == user!.Id
              select d;

        var devices = await q
            .Select(d => new { d.Id, d.Name, d.IsOnline, d.LastSeenAt })
            .ToListAsync();
        return Ok(devices);
    }

    // POST /api/devices/{id}/unlock actually triggers the lock.
    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id)
    {
        var user = await _users.GetUserAsync(User);
        var device = await _db.Devices.FindAsync(id);
        if (device is null) return NotFound();

        var isAdmin = await _users.IsInRoleAsync(user!, SeedData.AdminRole);
        UserDeviceAccess? access = null;
        if (!isAdmin)
        {
            access = await _db.UserDeviceAccess
                .FirstOrDefaultAsync(a => a.DeviceId == id && a.UserId == user!.Id);
            if (access is null) return Forbid();         // (b) row-level
            if (!IsWithinSchedule(access.ScheduleJson)) return Forbid(); // (c) schedule
        }

        // Audit BEFORE publishing: if the publish fails we still have the request logged.
        _db.EntryEvents.Add(new EntryEvent
        {
            DeviceId = id, UserId = user!.Id, Type = EntryEventType.UnlockRequested,
        });
        await _db.SaveChangesAsync();

        await _mqtt.PublishUnlockAsync(device.MqttTopic, user.Id);
        return Accepted();   // 202: we sent the command; the actual grant/deny comes back via MQTT
    }

    // GET /api/devices/{id}/events returns the entry log
    [HttpGet("{id:int}/events")]
    public async Task<IActionResult> Events(int id, int take = 50)
    {
        var user = await _users.GetUserAsync(User);
        var isAdmin = await _users.IsInRoleAsync(user!, SeedData.AdminRole);
        if (!isAdmin && !await _db.UserDeviceAccess.AnyAsync(a => a.DeviceId == id && a.UserId == user!.Id))
            return Forbid();

        var events = await _db.EntryEvents
            .Where(e => e.DeviceId == id)
            .OrderByDescending(e => e.OccurredAt)
            .Take(Math.Min(take, 500))
            .Select(e => new { e.Id, e.Type, e.OccurredAt, e.Note, User = e.User == null ? null : e.User.DisplayName })
            .ToListAsync();
        return Ok(events);
    }

    // ---------- Admin-only: manage device access ----------
    public record GrantDto(string UserId, string ScheduleJson);

    [HttpPost("{id:int}/access")]
    [Authorize(Roles = SeedData.AdminRole)]
    public async Task<IActionResult> GrantAccess(int id, GrantDto dto)
    {
        var existing = await _db.UserDeviceAccess.FirstOrDefaultAsync(a => a.DeviceId == id && a.UserId == dto.UserId);
        if (existing is null)
            _db.UserDeviceAccess.Add(new UserDeviceAccess { DeviceId = id, UserId = dto.UserId, ScheduleJson = dto.ScheduleJson });
        else
            existing.ScheduleJson = dto.ScheduleJson;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // -------- schedule check ------------
    // ScheduleJson format: {"mon":[["07:00","22:00"]], "tue":[...], ...}
    private static bool IsWithinSchedule(string scheduleJson)
    {
        if (string.IsNullOrWhiteSpace(scheduleJson) || scheduleJson == "{}") return true; // no restrictions
        var now = DateTimeOffset.Now;
        var key = now.DayOfWeek.ToString().Substring(0, 3).ToLowerInvariant(); // "mon"
        using var doc = JsonDocument.Parse(scheduleJson);
        if (!doc.RootElement.TryGetProperty(key, out var ranges)) return false;
        var t = now.TimeOfDay;
        foreach (var range in ranges.EnumerateArray())
        {
            var start = TimeSpan.Parse(range[0].GetString()!);
            var end   = TimeSpan.Parse(range[1].GetString()!);
            if (t >= start && t <= end) return true;
        }
        return false;
    }
}


