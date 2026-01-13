using Microsoft.AspNetCore.Identity;

namespace ScreenBux.WebServer.Models;

public class ApplicationUser : IdentityUser
{
    public ICollection<UserHousehold> UserHouseholds { get; set; } = new List<UserHousehold>();
}
