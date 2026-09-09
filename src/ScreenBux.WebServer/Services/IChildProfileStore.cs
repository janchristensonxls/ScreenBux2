using ScreenBux.Shared.Models.Devices;

namespace ScreenBux.WebServer.Services;

/// <summary>Abstraction over <c>ChildProfile</c> persistence, scoped per account.</summary>
public interface IChildProfileStore
{
    Task<IReadOnlyList<ChildProfileDto>> GetProfilesAsync(string accountId, CancellationToken cancellationToken = default);

    Task<ChildProfileDto> CreateProfileAsync(string accountId, string displayName, CancellationToken cancellationToken = default);

    Task<ChildProfileDto?> UpdateProfileAsync(string accountId, Guid childProfileId, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the profile. Returns false if not found or it still has linked devices.</summary>
    Task<bool> DeleteProfileAsync(string accountId, Guid childProfileId, CancellationToken cancellationToken = default);
}
