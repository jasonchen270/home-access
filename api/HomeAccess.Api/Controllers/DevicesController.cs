// Authorization is layered: role check on the endpoint, row-level filter in the
// query (UserDeviceAccess), and a schedule check at unlock time. Each gate is
// independent so bypassing one does not grant access.

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
[Authorize]
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

    // Admins see all devices; members see only those in UserDeviceAccess.
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var user = await _users.GetUserAsync(User);
        var isAdmin = await _users.IsInRoleAsync(user!, SeedData.AdminRole);

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
            if (access is null) return Forbid();                          // row-level
            if (!IsWithinSchedule(access.ScheduleJson)) return Forbid();  // schedule
        }

        // Log the attempt before publishing so a failed publish is still recorded.
        _db.EntryEvents.Add(new EntryEvent
        {
            DeviceId = id, UserId = user!.Id, Type = EntryEventType.UnlockRequested,
        });
        await _db.SaveChangesAsync();

        await _mqtt.PublishUnlockAsync(device.MqttTopic, user.Id);
        return Accepted();   // grant/deny comes back asynchronously via MQTT
    }

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

    // ScheduleJson format: {"mon":[["07:00","22:00"]], "tue":[...], ...}. Empty means no restriction.
    private static bool IsWithinSchedule(string scheduleJson)
    {
        if (string.IsNullOrWhiteSpace(scheduleJson) || scheduleJson == "{}") return true;
        var now = DateTimeOffset.Now;
        var key = now.DayOfWeek.ToString().Substring(0, 3).ToLowerInvariant();
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
