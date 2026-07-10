using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ScreenBux.Data.Entities;
using ScreenBux.Shared.Models.Auth;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly ILogger<AccountController> _logger;
    private readonly UserManager<Account> _userManager;
    private readonly JwtTokenService _tokenService;
    private readonly IConfiguration _configuration;

    public AccountController(
        ILogger<AccountController> logger,
        UserManager<Account> userManager,
        JwtTokenService tokenService,
        IConfiguration configuration)
    {
        _logger = logger;
        _userManager = userManager;
        _tokenService = tokenService;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var account = new Account
        {
            UserName = request.Email,
            Email = request.Email
        };

        var result = await _userManager.CreateAsync(account, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = "Registration failed.", errors = result.Errors.Select(e => e.Description) });
        }

        _logger.LogInformation("Account registered for {Email}", request.Email);
        return Ok(BuildResponse(account));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var account = await _userManager.FindByEmailAsync(request.Email);
        if (account is null || !await _userManager.CheckPasswordAsync(account, request.Password))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        _logger.LogInformation("Account logged in for {Email}", request.Email);
        return Ok(BuildResponse(account));
    }

    private AuthResponse BuildResponse(Account account)
    {
        var minutes = int.TryParse(_configuration["Jwt:AccessTokenLifetimeMinutes"], out var m) ? m : 60;
        return new AuthResponse
        {
            Token = _tokenService.CreateAccountToken(account.Id, account.Email ?? string.Empty),
            Email = account.Email ?? string.Empty,
            ExpiresAt = DateTime.UtcNow.AddMinutes(minutes)
        };
    }
}
