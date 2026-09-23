metadata description = 'Service Bus namespace and the integration-event topic (the cloud half of ADR-06).'

param resourcePrefix string
param location string
param tags object
param skuName string

@description('One subscription per consuming service, so each gets its own copy and its own dead-letter queue.')
var subscriptions = [
  'corehr'
  'time'
  'payroll'
  'talent'
  'spend'
  'workflow'
  'notification'
  'document'
  'reporting'
  'integration'
]

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${resourcePrefix}-bus'
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    minimumTlsVersion: '1.2'
    // Managed identity only; no shared access keys to distribute or rotate.
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
    zoneRedundant: skuName == 'Premium'
  }
}

resource eventTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: namespace
  name: 'gggs-events'
  properties: {
    // Messages persist until a subscriber takes them; a service being down for a deploy must not
    // cost it events.
    defaultMessageTimeToLive: 'P14D'
    enablePartitioning: skuName != 'Premium'
    supportOrdering: true
  }
}

resource topicSubscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = [
  for subscriptionName in subscriptions: {
    parent: eventTopic
    name: subscriptionName
    properties: {
      // Delivery is at-least-once and consumers are idempotent (they keep an inbox), so a handler
      // that keeps failing should stop being retried and become visible instead.
      maxDeliveryCount: 5
      deadLetteringOnMessageExpiration: true
      lockDuration: 'PT1M'
      defaultMessageTimeToLive: 'P14D'
    }
  }
]

output namespaceName string = namespace.name
output topicName string = eventTopic.name
