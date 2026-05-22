// =============================================================================
// SeedData.cs runs once on startup to ensure roles + an admin user + sample
// devices exist. This is for LEARNING; in real apps seed via migrations or a
// separate CLI tool.
// =============================================================================

using HomeAccess.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeAccess.Api.Data;

public static class SeedData
{
    public const string AdminRole = "Admin";   // can manage devices + users
    public const string MemberRole = "Member"; // household member, can only unlock allowed devices

    public static async Task RunAsync(IServiceProvider sp)
    {
        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var db = sp.GetRequiredService<AppDbContext>();

        foreach (var r in new[] { AdminRole, MemberRole })
            if (!await roleMgr.RoleExistsAsync(r))
                await roleMgr.CreateAsync(new IdentityRole(r));

        // Default admin: admin@home.local / Admin123!
        var admin = await userMgr.FindByEmailAsync("admin@home.local");
        if (admin is null)
        {
            admin = new AppUser { UserName = "admin@home.local", Email = "admin@home.local", DisplayName = "Admin" };
            await userMgr.CreateAsync(admin, "Admin123!");
            await userMgr.AddToRoleAsync(admin, AdminRole);
        }

        if (!await db.Devices.AnyAsync())
        {
            db.Devices.AddRange(
                new Device { Name = "Front Door", MqttTopic = "home/door/front" },
                new Device { Name = "Garage",     MqttTopic = "home/door/garage" });
            await db.SaveChangesAsync();
        }
    }
}
