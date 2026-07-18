using Npgsql;

namespace TriageBot.Infrastructure.Persistence;

/// <summary>
/// Normalizes a database connection string into the key-value form Npgsql expects.
/// <para>
/// Managed Postgres providers (Neon, Supabase, Railway, …) usually hand you a URL such as
/// <c>postgresql://user:pass@host/db?sslmode=require</c>. Npgsql does not parse that URL form,
/// so this converts it to <c>Host=…;Port=…;Database=…;Username=…;Password=…;SSL Mode=Require;Trust Server Certificate=true</c>.
/// A string that is already in key-value form is returned unchanged, so either format works in
/// <c>ConnectionStrings__TriageBotDb</c>.
/// </para>
/// </summary>
public static class NpgsqlConnectionString
{
    /// <summary>Converts a Neon/Postgres URL to Npgsql key-value form; passes key-value strings through unchanged.</summary>
    public static string Normalize(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("The connection string is empty.", nameof(connectionString));

        var trimmed = connectionString.Trim();

        var isUrl = trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                 || trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
        if (!isUrl)
            return trimmed; // already Npgsql key-value form — leave the user's settings intact.

        var uri = new Uri(trimmed);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            // Serverless providers idle-suspend; a generous connect timeout lets the first
            // request survive a cold start instead of failing immediately.
            Timeout = 15
        };

        // Default to encrypted transport (managed providers require it). sslmode in the URL wins.
        var sslMode = SslMode.Require;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
            if (string.Equals(key, "sslmode", StringComparison.OrdinalIgnoreCase))
                sslMode = MapSslMode(value);
        }

        // In Npgsql 8+, SslMode.Require already means "encrypt but don't validate the certificate
        // chain" (the legacy Trust Server Certificate flag is now a no-op), which is what Neon needs.
        // VerifyCA/VerifyFull opt into chain validation.
        builder.SslMode = sslMode;

        return builder.ConnectionString;
    }

    private static SslMode MapSslMode(string value) => value.ToLowerInvariant() switch
    {
        "disable" => SslMode.Disable,
        "allow" => SslMode.Allow,
        "prefer" => SslMode.Prefer,
        "require" => SslMode.Require,
        "verify-ca" => SslMode.VerifyCA,
        "verify-full" => SslMode.VerifyFull,
        _ => SslMode.Require
    };
}
