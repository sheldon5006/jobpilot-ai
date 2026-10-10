using Npgsql;

namespace JobPilot.Api.Data;

public static class PostgresConnection
{
    /// <summary>
    /// Converts a postgres:// URI (as provided by Neon or Render) into an Npgsql connection string.
    /// Key/value connection strings are returned unchanged. TLS is required unless the URI sets sslmode=disable.
    /// </summary>
    public static string FromUrl(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)))
        {
            return databaseUrl;
        }

        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2)
        {
            throw new InvalidOperationException("DATABASE_URL must include a PostgreSQL username and password.");
        }

        var sslDisabled = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(pair => pair.Equals("sslmode=disable", StringComparison.OrdinalIgnoreCase));

        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            SslMode = sslDisabled ? SslMode.Disable : SslMode.Require,
            Timeout = 15,
            CommandTimeout = 30,
            Pooling = true
        }.ConnectionString;
    }
}
