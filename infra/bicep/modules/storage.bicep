metadata description = 'Blob storage for employee documents. One container per tenant, created by the provisioning saga.'

param resourcePrefix string
param location string
param tags object
param skuName string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  // Storage account names allow no separators and are limited to 24 characters.
  name: replace('${resourcePrefix}docs', '-', '')
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true

    // A public container here would turn every document URL into a data breach, so the account
    // cannot allow one even by mistake.
    allowBlobPublicAccess: false

    // Keys disabled: downloads are served through short-lived user-delegation SAS tokens signed
    // with managed identity, so there is no account key to leak or rotate.
    allowSharedKeyAccess: false

    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    // Versioning plus soft delete: an employee document deleted in error is recoverable, and a
    // ransomware overwrite does not destroy the previous content.
    isVersioningEnabled: true
    deleteRetentionPolicy: {
      enabled: true
      days: 30
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 30
    }
    changeFeed: {
      enabled: true
    }
  }
}

// Quarantine container: uploads land here and only move to the tenant's own container once the
// malware scan reports clean (§15).
resource quarantineContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'quarantine'
  properties: {
    publicAccess: 'None'
  }
}

output name string = storageAccount.name
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
