using System.Text.RegularExpressions;

namespace Dms.Infrastructure.Persistence;

/// <summary>
/// The database role the API and the worker connect as (section 4.10). The migrator connects as
/// the owner, applies the schema, and then grants this role its rights.
/// </summary>
public sealed class DatabaseRoleOptions
{
    public const string SectionName = "Dms:Database";

    /// <summary>The runtime role. Empty leaves grants alone (a DBA manages them).</summary>
    public string? AppRole { get; set; } = "dms_app";

    /// <summary>Sets the role's password when given. Without it the role is created without one.</summary>
    public string? AppRolePassword { get; set; }

    /// <summary>Whether the migrator may create the role. Off when the DBA creates roles.</summary>
    public bool CreateAppRole { get; set; } = true;
}

internal static partial class RuntimeRoleSql
{
    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex IdentifierPattern();

    public static string QuoteIdentifier(string name) =>
        IdentifierPattern().IsMatch(name)
            ? $"\"{name}\""
            : throw new InvalidOperationException($"'{name}' is not a valid role name (lower case letters, digits and underscores).");

    private static string QuoteLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    public static IEnumerable<string> Build(string role, DatabaseRoleOptions options, IReadOnlyList<IModuleDatabase> modules)
    {
        var name = QuoteLiteral(options.AppRole!);
        if (options.CreateAppRole)
        {
            // Another migrator (or test run) may create it at the same moment.
            yield return $"""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = {name}) THEN
                        CREATE ROLE {role} LOGIN;
                    END IF;
                EXCEPTION WHEN duplicate_object THEN NULL;
                END
                $$;
                """;

            yield return $"ALTER ROLE {role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;";
        }

        if (!string.IsNullOrEmpty(options.AppRolePassword))
        {
            yield return $"ALTER ROLE {role} PASSWORD {QuoteLiteral(options.AppRolePassword)};";
        }

        yield return $"""
            DO $$
            BEGIN
                EXECUTE format('GRANT CONNECT, TEMPORARY ON DATABASE %I TO %I', current_database(), {name});
            END
            $$;
            """;

        foreach (var module in modules)
        {
            var schema = QuoteIdentifier(module.Name);
            var tables = module.RuntimeAccess == RuntimeAccess.AppendOnly ? "SELECT, INSERT" : "SELECT, INSERT, UPDATE, DELETE";

            yield return $"""
                GRANT USAGE ON SCHEMA {schema} TO {role};
                REVOKE CREATE ON SCHEMA {schema} FROM {role};
                REVOKE ALL ON ALL TABLES IN SCHEMA {schema} FROM {role};
                GRANT {tables} ON ALL TABLES IN SCHEMA {schema} TO {role};
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO {role};
                GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA {schema} TO {role};
                REVOKE INSERT, UPDATE, DELETE ON TABLE {schema}."__ef_migrations_history" FROM {role};
                """;
        }
    }
}
