using Npgsql;

namespace CareerConnect.Api.Data;

/// <summary>
/// Resolves the Postgres connection string. Most managed Postgres add-ons
/// (Railway, Render, Heroku-style platforms) inject a single `DATABASE_URL`
/// env var as a `postgres://user:pass@host:port/db` URI rather than Npgsql's
/// own `Host=...;Username=...` syntax, so that shape is converted here.
/// </summary>
public static class PostgresConnectionString
{
    public static string Resolve(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            throw new InvalidOperationException(
                "No database connection configured. Set ConnectionStrings:Default " +
                "(Npgsql format) or a DATABASE_URL environment variable (postgres:// URI).");
        }

        return FromUri(databaseUrl);
    }

    private static string FromUri(string databaseUrl)
    {
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            SslMode = SslMode.Require,
        };

        return builder.ConnectionString;
    }
}
