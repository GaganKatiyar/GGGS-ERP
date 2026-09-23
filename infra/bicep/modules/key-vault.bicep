metadata description = 'Key Vault holding tenant connection strings, per-tenant data keys and integration credentials.'

param resourcePrefix string
param location string
param tags object
param enablePurgeProtection bool

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${resourcePrefix}-kv'
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId

    // RBAC rather than access policies: role assignments are auditable in one place and can be
    // scoped to an individual secret.
    enableRbacAuthorization: true

    // Soft delete cannot be turned off, and should not be: destroying a tenant's data key by
    // accident would make their encrypted PII unrecoverable.
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: enablePurgeProtection ? true : null

    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

output name string = keyVault.name
output uri string = keyVault.properties.vaultUri
