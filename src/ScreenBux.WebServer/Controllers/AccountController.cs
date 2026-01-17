using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ScreenBux.WebServer.Contracts;
using ScreenBux.WebServer.Data;
using ScreenBux.WebServer.Models;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _dbContext;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext dbContext)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        var household = new Household
        {
            Name = string.IsNullOrWhiteSpace(request.HouseholdName) ? "My Household" : request.HouseholdName
        };

        _dbContext.Households.Add(household);
        _dbContext.UserHouseholds.Add(new UserHousehold
        {
            UserId = user.Id,
            HouseholdId = household.HouseholdId,
            Role = HouseholdRole.Admin
        });

        await _dbContext.SaveChangesAsync();
        await _signInManager.SignInAsync(user, isPersistent: true);

        return Ok(new AuthResponse(user.Id, user.Email ?? string.Empty));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await _signInManager.PasswordSignInAsync(request.Email, request.Password, true, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return Unauthorized();
        }

        return Ok(new AuthResponse(user.Id, user.Email ?? string.Empty));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Ok();
    }
}
