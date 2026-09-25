using System.Security.Cryptography;
using Ilm.Application.Audit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ilm.Infrastructure.Audit;

public sealed class AuditOptions
{
    public const string SectionName = "Ilm:Audit";

    /// <summary>Environment variable holding the base64 HMAC key (production: injected from a vault, never in files).</summary>
    public string KeyEnvironmentVariable { get; set; } = "ILM_AUDIT_HMAC_KEY";

    public string KeyId { get; set; } = "audit-key-1";

    /// <summary>Development only: a generated key file outside source control.</summary>
    public string DevelopmentKeyPath { get; set; } = "App_Data/audit-dev.key";

    /// <summary>Append-only JSONL sink path (development and file-based forwarding).</summary>
    public string? JsonlSinkPath { get; set; } = "App_Data/audit-forwarded.jsonl";

    /// <summary>Optional SIEM HTTP collector endpoint. Its bearer token is read from <see cref="SiemTokenEnvironmentVariable"/>.</summary>
    public string? SiemEndpoint { get; set; }

    public string SiemTokenEnvironmentVariable { get; set; } = "ILM_SIEM_TOKEN";

    public int MaxForwardingLag { get; set; } = 1000;
}

/// <summary>
/// Supplies the audit HMAC key from outside the database: an environment variable (injected from a vault in
/// production) or, in Development only, a generated local key file.
/// </summary>
public sealed class AuditKeyProvider : IAuditKeyProvider
{
    private readonly AuditKey key;

    public AuditKeyProvider(IOptions<AuditOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        var o = options.Value;
        var fromEnv = Environment.GetEnvironmentVariable(o.KeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var material = Convert.FromBase64String(fromEnv);
            if (material.Length < 32)
            {
                throw new InvalidOperationException("The audit HMAC key must be at least 256 bits.");
            }

            key = new AuditKey(o.KeyId, material);
            return;
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException($"No audit HMAC key: set {o.KeyEnvironmentVariable} from the secret store.");
        }

        var path = Path.IsPathRooted(o.DevelopmentKeyPath) ? o.DevelopmentKeyPath : Path.Combine(environment.ContentRootPath, o.DevelopmentKeyPath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        }

        key = new AuditKey("dev-" + o.KeyId, Convert.FromBase64String(File.ReadAllText(path).Trim()));
    }

    public AuditKey CurrentKey => key;

    public AuditKey? FindKey(string keyId) => string.Equals(keyId, key.KeyId, StringComparison.Ordinal) ? key : null;
}

/// <summary>A fixed key for tests.</summary>
public sealed class StaticAuditKeyProvider(byte[] material, string keyId = "test-key") : IAuditKeyProvider
{
    public AuditKey CurrentKey { get; } = new(keyId, material);

    public AuditKey? FindKey(string keyId) => string.Equals(keyId, CurrentKey.KeyId, StringComparison.Ordinal) ? CurrentKey : null;
}
