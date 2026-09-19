using Shouldly;

namespace Dms.IntegrationTests;

internal static class AuditAssertions
{
    /// <summary>Asserts that the given action was recorded. Audit rows are part of the contract.</summary>
    public static async Task ShouldHaveAuditAsync(this DmsApiFactory factory, string action)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM audit.audit_logs WHERE action = @action;";
        command.Parameters.AddWithValue("action", action);

        var count = (long)(await command.ExecuteScalarAsync())!;
        count.ShouldBeGreaterThan(0, $"expected an audit row for '{action}'");
    }
}
