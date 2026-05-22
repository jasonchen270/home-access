// =============================================================================
// AuthController.cs handles login / logout / "who am I". Uses ASP.NET Identity's
// SignInManager which handles the cookie issuance for us.
//
// LEARNING NOTES:
//   - [ApiController] enables automatic model validation + 400 responses.
//   - [Route("api/[controller]")] → "[controller]" is replaced with the class
//     name minus "Controller", so this becomes /api/auth.
//   - Constructor injection: ASP.NET's DI container sees these parameters and
//     supplies registered services. There's no `new AuthController(...)` anywhere.
// =============================================================================

using HomeAccess.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HomeAccess.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly UserManager<AppUser> _users;

    public AuthController(SignInManager<AppUser> signIn, UserManager<AppUser> users)
    {
        _signIn = signIn;
        _users = users;
    }

    public record LoginDto(string Email, string Password);

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        // PasswordSignInAsync handles hash comparison, lockout, and cookie issuance.
        var result = await _signIn.PasswordSignInAsync(dto.Email, dto.Password, isPersistent: true, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized();
        return Ok();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return Ok();
    }

    [Authorize]   // [Authorize] = "must be signed in". Without it, anonymous works.
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _users.GetUserAsync(User);    // `User` = ClaimsPrincipal from the cookie
        if (user is null) return Unauthorized();
        var roles = await _users.GetRolesAsync(user);
        return Ok(new { user.Id, user.Email, user.DisplayName, Roles = roles });
    }
}
