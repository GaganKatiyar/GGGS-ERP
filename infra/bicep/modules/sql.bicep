metadata description = 'Azure SQL server and the elastic pool that holds the pooled-tier tenant databases.'

param resourcePrefix string
param location string
param tags object
param administratorLogin string

@secure()
param administratorPassword string

@description('Entra ID group set as the server administrator, so no individual is the owner.')
param adminGroupObjectId string

param elasticPoolSku object
param zoneRedundant bool
param logAnalyticsWorkspaceId string

@description('One database per service (ADR-03). Tenants share these on the pooled tier.')
// One application database (ADR-12): every module's schema lives here. Dedicated-tier tenants
// get their own copy of this same database, selected through the tenant catalog (ADR-01).
var serviceDatabases = [
  'peopleos'
]

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${resourcePrefix}-sql'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    version: '12.0'
    // No public access: services reach the server over a private endpoint, and administrators
    // through the jump path. A connection string alone is not enough to reach HR data.
    publicNetworkAccess: 'Disabled'
    minimalTlsVersion: '1.2'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: 'gggs-sql-admins'
      sid: adminGroupObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: false
    }
  }
}

resource elasticPool 'Microsoft.Sql/servers/elasticPools@2023-08-01-preview' = {
  parent: sqlServer
  name: '${resourcePrefix}-pool'
  location: location
  tags: tags
  sku: elasticPoolSku
  properties: {
    zoneRedundant: zoneRedundant
    // Per-database ceiling inside the pool: one tenant running a heavy report cannot consume the
    // whole pool and stall every other tenant on it (the noisy-neighbour control from §18).
    perDatabaseSettings: {
      minCapacity: 0
      maxCapacity: 2
    }
    licenseType: 'LicenseIncluded'
  }
}

resource databases 'Microsoft.Sql/servers/databases@2023-08-01-preview' = [
  for databaseName in serviceDatabases: {
    parent: sqlServer
    name: databaseName
    location: location
    tags: tags
    sku: {
      name: 'ElasticPool'
      tier: 'ElasticPool'
    }
    properties: {
      elasticPoolId: elasticPool.id
      collation: 'SQL_Latin1_General_CP1_CI_AS'
      // Transparent data encryption is on by default; the dedicated tier can swap in a
      // customer-managed key later without changing the schema.
      zoneRedundant: zoneRedundant
    }
  }
]

// Long-term retention: payroll and audit data outlive the 35-day point-in-time window by years.
resource retentionPolicies 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2023-08-01-preview' = [
  for (databaseName, index) in serviceDatabases: {
    parent: databases[index]
    name: 'default'
    properties: {
      weeklyRetention: 'P12W'
      monthlyRetention: 'P12M'
      yearlyRetention: 'P7Y'
      weekOfYear: 1
    }
  }
]

resource auditSettings 'Microsoft.Sql/servers/auditingSettings@2023-08-01-preview' = {
  parent: sqlServer
  name: 'default'
  properties: {
    state: 'Enabled'
    isAzureMonitorTargetEnabled: true
    retentionDays: 90
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: sqlServer
  name: 'audit-to-log-analytics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'SQLSecurityAuditEvents'
        enabled: true
      }
    ]
  }
}

output serverName string = sqlServer.name
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output elasticPoolName string = elasticPool.name
