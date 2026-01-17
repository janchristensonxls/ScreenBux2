using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenBux.WebServer.Data;
using ScreenBux.WebServer.Models;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Route("api/households")]
[Authorize(Policy = "UserOnly")]
public class HouseholdsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public HouseholdsController(AppDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetHouseholds()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var households = await _dbContext.UserHouseholds
            .Where(uh => uh.UserId == user.Id)
            .Select(uh => new
            {
                uh.HouseholdId,
                uh.Household!.Name,
                Role = uh.Role.ToString()
            })
            .ToListAsync();

        return Ok(households);
    }
}
