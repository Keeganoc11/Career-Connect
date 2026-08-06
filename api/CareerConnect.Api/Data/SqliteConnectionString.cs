using Microsoft.Data.Sqlite;

namespace CareerConnect.Api.Data;

/// <summary>
/// Resolves the SQLite file to an absolute path anchored at the app's content
/// root.
///
/// A bare "Data Source=careerconnect.db" resolves relative to the *current
/// working directory*, so launching from the repo root, an IDE debugger, or a
/// published folder each silently creates a different, empty database — which
/// looks exactly like data loss. Anchoring to the content root means one file
/// no matter where the process is started from.
/// </summary>
public static class SqliteConnectionString
{
    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration.GetConnectionString("Default")
            ?? "Data Source=careerconnect.db";

        var builder = new SqliteConnectionStringBuilder(configured);

        // In-memory databases (used by tests) have no file to anchor.
        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase) ||
            builder.Mode == SqliteOpenMode.Memory)
        {
            return configured;
        }

        if (!Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.Combine(environment.ContentRootPath, builder.DataSource);
        }

        return builder.ToString();
    }
}
