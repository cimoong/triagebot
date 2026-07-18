using Npgsql;
using TriageBot.Infrastructure.Persistence;

namespace TriageBot.Tests;

public class NpgsqlConnectionStringTests
{
    // A fake Neon-style URL (no real credentials) — mirrors the format managed providers hand out.
    private const string NeonStyleUrl =
        "postgresql://neondb_owner:npg_fakeSecret123@ep-demo-host-123.ap-southeast-1.aws.neon.tech/neondb?sslmode=require";

    [Fact]
    public void Normalize_ConvertsNeonUrl_ToNpgsqlKeyValueForm()
    {
        var result = NpgsqlConnectionString.Normalize(NeonStyleUrl);

        // Parse the result back so we assert on values, not string formatting.
        var b = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("ep-demo-host-123.ap-southeast-1.aws.neon.tech", b.Host);
        Assert.Equal(5432, b.Port); // no port in the URL -> Postgres default
        Assert.Equal("neondb", b.Database);
        Assert.Equal("neondb_owner", b.Username);
        Assert.Equal("npg_fakeSecret123", b.Password);
        Assert.Equal(SslMode.Require, b.SslMode);
    }

    [Fact]
    public void Normalize_RespectsExplicitPort()
    {
        var result = NpgsqlConnectionString.Normalize(
            "postgres://user:pass@localhost:6543/mydb?sslmode=require");

        var b = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal(6543, b.Port);
        Assert.Equal("mydb", b.Database);
    }

    [Fact]
    public void Normalize_LeavesKeyValueStringUnchanged()
    {
        const string keyValue = "Host=localhost;Port=5433;Database=triagebot;Username=postgres;Password=postgres";

        Assert.Equal(keyValue, NpgsqlConnectionString.Normalize(keyValue));
    }

    [Fact]
    public void Normalize_MapsVerifyFull_ToValidatingSslMode()
    {
        var result = NpgsqlConnectionString.Normalize(
            "postgresql://user:pass@host.example.com/db?sslmode=verify-full");

        Assert.Equal(SslMode.VerifyFull, new NpgsqlConnectionStringBuilder(result).SslMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Throws_OnEmpty(string value)
    {
        Assert.Throws<ArgumentException>(() => NpgsqlConnectionString.Normalize(value));
    }
}
