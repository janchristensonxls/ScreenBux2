using ScreenBux.Shared.Models.Updates;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// Source of "what's the latest version of each component" for the auto-update system.
/// This is intentionally a thin abstraction: a real deployment might back this with a
/// release/CI feed or a database table, but the contract stays the same either way.
/// </summary>
public interface IUpdateManifestStore
{
    UpdateManifestDto GetLatestManifest();
}
