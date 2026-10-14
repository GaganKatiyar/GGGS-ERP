metadata description = 'GGGS HRMS shared infrastructure for one environment in one region.'

/*
  Scope note: this is the shared, per-environment infrastructure. Tenants are rows, not resources
  (ADR-01), so onboarding a customer deploys nothing. A tenant promoted to the dedicated tier gets
  its own database inside the same elastic pool, added by the provisioning saga rather than here.
*/

targetScope = 'resourceGroup'

@description('Environment name; becomes part of every resource name.')
@allowed(['dev', 'qa', 'uat', 'prod'])
param environmentName string

@description('Region for this deployment. Data residency is per region, so a tenant never leaves the one it was provisioned in.')
param location string = resourceGroup().location

@description('SQL administrator login. Only used to create the server; applications connect with managed identity.')
param sqlAdminLogin string

@secure()
@description('SQL administrator password. Supplied by the pipeline from Key Vault, never stored in a parameter file.')
param sqlAdminPassword string

@description('Entra ID group that owns the SQL server, so no human account is the owner.')
param sqlAdminGroupObjectId string

@description('Container image tag deployed by this run.')
param imageTag string = 'latest'

var isProduction = environmentName == 'prod'
var resourcePrefix = 'gggs-${environmentName}'

// Production gets zone redundancy and real capacity; lower environments get the cheapest thing
// that behaves the same, because a QA bill should not track a production one.
var sqlSku = isProduction
  ? { name: 'BC_Gen5', tier: 'BusinessCritical', family: 'Gen5', capacity: 8 }
  : { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }

var tags = {
  product: 'gggs-hrms'
  environment: environmentName
  managedBy: 'bicep'
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    resourcePrefix: resourcePrefix
    location: location
    tags: tags
    retentionDays: isProduction ? 90 : 30
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    resourcePrefix: resourcePrefix
    location: location
    tags: tags
    // Purge protection is irreversible, so it is production-only: a mistake in dev would otherwise
    // leave a vault name reserved for 90 days.
    enablePurgeProtection: isProduction
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    resourcePrefix: resourcePrefix
    location: location
    tags: tags
    administratorLogin: sqlAdminLogin
    administratorPassword: sqlAdminPassword
    adminGroupObjectId: sqlAdminGroupObjectId
    elasticPoolSku: sqlSku
    zoneRedundant: isProduction
    logAnalyticsWorkspaceId: monitoring.outputs.workspaceId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    resourcePrefix: resourcePrefix
    location: location
    tags: tags
    // Documents are irreplaceable; geo-redundancy in production is not optional.
    skuName: isProduction ? 'Standard_GZRS' : 'Standard_LRS'
  }
}

module serviceBus 'modules/service-bus.bicep' = {
  name: 'service-bus'
  params: {
    resourcePrefix: resourcePrefix
    location: location
    tags: tags
    // Premium for the isolation and zone redundancy production needs; Standard elsewhere.
    skuName: isProduction ? 'Premium' : 'Standard'
  }
}

module redis 'modules/redis.bicep' = {
  name: 'redis'
  params: {
    resourcePrefix: resourcePrefix
    location: location
    tags: tags
    capacity: isProduction ? 1 : 0
    skuName: isProduction ? 'Standard' : 'Basic'
  }
}

module apps 'modules/container-apps.bicep' = {
  name: 'container-apps'
  params: {
    resourcePrefix: resourcePrefix
    location: location
    tags: tags
    imageTag: imageTag
    logAnalyticsWorkspaceId: monitoring.outputs.workspaceId
    applicationInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
    keyVaultName: keyVault.outputs.name
    storageAccountName: storage.outputs.name
    serviceBusNamespace: serviceBus.outputs.namespaceName
    sqlServerName: sql.outputs.serverName
    zoneRedundant: isProduction
  }
}

output applicationUrl string = apps.outputs.apiUrl
output keyVaultName string = keyVault.outputs.name
output sqlServerName string = sql.outputs.serverName
