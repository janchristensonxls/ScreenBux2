using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenBux.WebServer.Contracts;
using ScreenBux.WebServer.Data;
using ScreenBux.WebServer.Models;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Route("api/pairing-codes")]
[Authorize(Policy = "UserOnly")]
public class PairingCodesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public PairingCodesController(AppDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreatePairingCodeRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var householdId = request.HouseholdId;
        var hasAccess = await _dbContext.UserHouseholds
            .AnyAsync(uh => uh.UserId == user.Id && uh.HouseholdId == householdId);
        if (!hasAccess)
        {
            return Forbid();
        }

        var code = GenerateCode();
        var pairingCode = new PairingCode
        {
            Code = code,
            HouseholdId = householdId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };

        _dbContext.PairingCodes.Add(pairingCode);
        await _dbContext.SaveChangesAsync();

        return Ok(new { code, pairingCode.ExpiresAt });
    }

    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = new byte[6];
        RandomNumberGenerator.Fill(bytes);
        var chars = bytes.Select(b => alphabet[b % alphabet.Length]).ToArray();
        return new string(chars);
    }
}
