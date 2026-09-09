using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenBux.Shared.Utilities;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for (de)serializing <c>PolicyConfiguration</c> and
/// its nested models everywhere it crosses a JSON boundary - the WebServer's DB-backed
/// <c>PolicyProfile.PolicyJson</c>, the WebClient policy editor, REST/SignalR payloads, and the
/// Service's local policy.json cache. Enums (e.g. <c>CategoryPolicyEnforcement</c>,
/// <c>PolicyRuleAction</c>, <c>DayOfWeek</c>) serialize as readable strings (e.g. "TimeLimited",
/// "Friday") instead of raw numbers, so hand-edited JSON templates stay human-friendly and
/// portable across every consumer.
/// </summary>
public static class PolicyJsonOptions
{
    public static readonly JsonSerializerOptions Default = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
