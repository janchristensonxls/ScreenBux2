namespace ScreenBux.WebServer.Models;

public class UserHousehold
{
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public Guid HouseholdId { get; set; }
    public Household? Household { get; set; }

    public HouseholdRole Role { get; set; } = HouseholdRole.Parent;
}
