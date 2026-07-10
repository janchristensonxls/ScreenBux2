using Microsoft.AspNetCore.Identity;

namespace ScreenBux.Data.Entities;

/// <summary>
/// A parent account. Extends ASP.NET Core Identity's user with domain data.
/// The Identity <see cref="IdentityUser.UserName"/>/<see cref="IdentityUser.Email"/>
/// are used for login; <see cref="IdentityUser.PasswordHash"/> holds the hashed password.
/// </summary>
public class Account : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ChildProfile> ChildProfiles { get; set; } = new List<ChildProfile>();

    public ICollection<Device> Devices { get; set; } = new List<Device>();

    public ICollection<PolicyDocument> PolicyDocuments { get; set; } = new List<PolicyDocument>();
}
