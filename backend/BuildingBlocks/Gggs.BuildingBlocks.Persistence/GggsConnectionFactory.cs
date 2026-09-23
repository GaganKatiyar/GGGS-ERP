using System.Data;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.Data.SqlClient;

namespace Gggs.BuildingBlocks.Persistence;

/// <summary>
/// Hands out database connections for the read path (Dapper), already scoped to the current tenant.
/// <para>
/// Read queries deliberately do not go through EF, but they must not lose isolation because of it.
/// Every connection this factory opens has the tenant published into session context first, so the
/// same row-level security policy applies to a hand-written query as to an EF one.
/// </para>
/// </summary>
public interface IGggsConnectionFactory
{
    /// <summary>Opens a connection with the tenant session context already set.</summary>
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlConnectionFactory(string connectionString, ITenantContext tenantContext) : IGggsConnectionFactory
{
    private const string SetSessionContextSql =
        "EXEC sys.sp_set_session_context @key = N'TenantId', @value = @tenantId, @read_only = 1;";

    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            if (!tenantContext.IsPlatformScope && tenantContext.IsResolved)
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = SetSessionContextSql;
                command.Parameters.AddWithValue("@tenantId", tenantContext.TenantId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
