metadata description = 'Container Apps environment plus the API and worker apps (ADR-11: cloud profile).'

param resourcePrefix string
param location string
param tags object
param imageTag string
param logAnalyticsWorkspaceId string

@secure()
param applicationInsightsConnectionString string

param keyVaultName string
param storageAccountName string
param serviceBusNamespace string
param sqlServerName string
param zoneRedundant bool

@description('Registry holding the service images.')
param containerRegistry string = '${replace(resourcePrefix, '-', '')}acr.azurecr.io'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: last(split(logAnalyticsWorkspaceId, '/'))
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${resourcePrefix}-env'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    zoneRedundant: zoneRedundant
  }
}

// One identity shared by the services in this environment. It is the principal that reads Key
// Vault, writes blobs and connects to SQL, so there are no connection secrets to distribute.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${resourcePrefix}-identity'
  location: location
  tags: tags
}

var sharedEnvironmentVariables = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    // Selects the cloud adapters: Key Vault, Blob Storage and Service Bus.
    name: 'Gggs__Platform__DeploymentProfile'
    value: 'Cloud'
  }
  {
    name: 'Gggs__Secrets__KeyVault__Uri'
    value: 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}/'
  }
  {
    name: 'Gggs__Storage__Blob__ServiceUri'
    value: 'https://${storageAccountName}.blob.${az.environment().suffixes.storage}/'
  }
  {
    name: 'Gggs__Messaging__Namespace'
    value: '${serviceBusNamespace}.servicebus.windows.net'
  }
  {
    name: 'Gggs__Sql__ServerName'
    value: '${sqlServerName}${az.environment().suffixes.sqlServerHostname}'
  }
  {
    name: 'AZURE_CLIENT_ID'
    value: identity.properties.clientId
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: applicationInsightsConnectionString
  }
]

resource gateway 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${resourcePrefix}-gateway'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: containerRegistry
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'gateway'
          image: '${containerRegistry}/gateway:${imageTag}'
          env: sharedEnvironmentVariables
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        // Never scales to zero: a cold start on the first morning login is a bad first impression,
        // and the readiness probe would fail while the database connection warms up.
        minReplicas: zoneRedundant ? 3 : 1
        maxReplicas: 10
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${resourcePrefix}-worker'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      // No ingress: the worker drains the outbox and runs scheduled jobs, and nothing calls it.
      registries: [
        {
          server: containerRegistry
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${containerRegistry}/worker:${imageTag}'
          env: sharedEnvironmentVariables
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        // At least one replica always runs: the outbox relay is what turns "saved" into
        // "published", so a scaled-to-zero worker would stall every integration event.
        minReplicas: 1
        maxReplicas: 5
      }
    }
  }
}

output gatewayUrl string = 'https://${gateway.properties.configuration.ingress.fqdn}'
output identityPrincipalId string = identity.properties.principalId
output environmentName string = environment.name
