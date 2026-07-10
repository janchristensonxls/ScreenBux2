using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Data.Entities;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// EF Core-backed <see cref="IPolicyStore"/>. Stores each account's policy as serialized
/// <see cref="PolicyConfiguration"/> JSON in a <see cref="PolicyDocument"/> row.
/// </summary>
public class EfPolicyStore : IPolicyStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EfPolicyStore> _logger;

    public EfPolicyStore(AppDbContext db, IConfiguration configuration, ILogger<EfPolicyStore> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PolicyConfiguration> GetPolicyAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == null && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (document is not null)
        {
            return Deserialize(document.PolicyJson);
        }

        // First access: seed from legacy policy.json when available so nothing is lost.
        var seeded = LoadLegacyPolicyOrDefault();
        await SavePolicyAsync(accountId, seeded, cancellationToken);
        return seeded;
    }

    public async Task SavePolicyAsync(string accountId, PolicyConfiguration policy, CancellationToken cancellationToken = default)
    {
        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == null && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);

        var json = JsonSerializer.Serialize(policy, SerializerOptions);

        if (document is null)
        {
            document = new PolicyDocument
            {
                AccountId = accountId,
                PolicyJson = json,
                UpdatedAt = DateTime.UtcNow
            };
            _db.PolicyDocuments.Add(document);
        }
        else
        {
            document.PolicyJson = json;
            document.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PolicyConfiguration> GetDevicePolicyAsync(Guid deviceId, string accountId, CancellationToken cancellationToken = default)
    {
        var deviceDoc = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.DeviceId == deviceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (deviceDoc is not null)
        {
            return Deserialize(deviceDoc.PolicyJson);
        }

        return await GetPolicyAsync(accountId, cancellationToken);
    }

    private PolicyConfiguration LoadLegacyPolicyOrDefault()
    {
        try
        {
            var policyPath = _configuration["PolicyFilePath"] ?? PolicyStorage.GetDefaultPolicyPath();
            if (File.Exists(policyPath))
            {
                var json = File.ReadAllText(policyPath);
                var legacy = Deserialize(json);
                _logger.LogInformation("Seeded policy from legacy file {Path}", policyPath);
                return legacy;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed policy from legacy policy.json; using defaults.");
        }

        return new PolicyConfiguration();
    }

    private static PolicyConfiguration Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PolicyConfiguration();
        }

        return JsonSerializer.Deserialize<PolicyConfiguration>(json) ?? new PolicyConfiguration();
    }
}
