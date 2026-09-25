#:package Npgsql@10.0.3
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false

// Creates or drops the throwaway database of scripts/e2e.sh, so the script needs no psql.
// Usage: dotnet run scripts/tools/e2e-database.cs -- <create|drop> <server connection string> <name>
using System.Text.RegularExpressions;
using Npgsql;

if (args is not [var action and ("create" or "drop"), var server, var name] || !Regex.IsMatch(name, "^dms_e2e_[a-z0-9_]{1,40}$"))
{
    Console.Error.WriteLine("Usage: e2e-database.cs <create|drop> <server connection string> <dms_e2e_name>");
    return 2;
}

await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
await connection.OpenAsync();
await using var command = connection.CreateCommand();
command.CommandText = action == "create"
    ? $"CREATE DATABASE \"{name}\""
    : $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";
await command.ExecuteNonQueryAsync();
return 0;
