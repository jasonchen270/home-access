using HomeAccess.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeAccess.Api.Data;

// Ensures roles, an admin user, and sample devices exist on first run.
public static class SeedData
{
    public const string AdminRole = "Admin";    // manage devices + users
    public const string MemberRole = "Member";  // unlock allowed devices only

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
