using System.Data.Common;
using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gggs.BuildingBlocks.Persistence.Interceptors;

/// <summary>
/// Publishes the current tenant into SQL Server's <c>SESSION_CONTEXT</c> whenever a connection is
/// opened, which is what the row-level security policy reads.
/// <para>
/// This is the layer that protects the data even when the application layer is wrong: raw SQL, a
/// Dapper query with a forgotten WHERE clause, a missing EF filter or an injection all still see
/// only the current tenant's rows. The value is written with <c>@read_only = 1</c> so nothing later
/// in the session can change it.
/// </para>
/// <para>
/// Connection pooling resets session context on reuse (<c>sp_reset_connection</c>), so it is set on
/// every open rather than once per connection object.
/// </para>
/// </summary>
public sealed class TenantSessionContextInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    private const string SetSessionContextSql =
        "EXEC sys.sp_set_session_context @key = N'TenantId', @value = @tenantId, @read_only = 1;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (TryGetTenantId(out Guid tenantId))
        {
            using DbCommand command = CreateCommand(connection, tenantId);
            command.ExecuteNonQuery();
        }

        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (TryGetTenantId(out Guid tenantId))
        {
            await using DbCommand command = CreateCommand(connection, tenantId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    /// <summary>
    /// Platform-scope work leaves the session context unset on purpose: those connections use the
    /// bypass principal, and setting a tenant would only narrow what provisioning can see.
    /// </summary>
    private bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = Guid.Empty;

        if (tenantContext.IsPlatformScope || !tenantContext.IsResolved)
        {
            return false;
        }

        tenantId = tenantContext.TenantId;
        return true;
    }

    private static DbCommand CreateCommand(DbConnection connection, Guid tenantId)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = SetSessionContextSql;

        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@tenantId";
        parameter.Value = tenantId;
        command.Parameters.Add(parameter);

        return command;
    }
}
