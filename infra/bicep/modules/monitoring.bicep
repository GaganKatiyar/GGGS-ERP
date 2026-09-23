metadata description = 'Log Analytics and Application Insights. Services emit OTLP; this is where it lands in the cloud.'

param resourcePrefix string
param location string
param tags object
param retentionDays int

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${resourcePrefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionDays
    features: {
      // Telemetry can contain tenant identifiers, so cross-workspace querying stays off.
      disableLocalAuth: true
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${resourcePrefix}-insights'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    // Instrumentation keys are deprecated; services use the connection string with managed identity.
    DisableLocalAuth: false
    IngestionMode: 'LogAnalytics'
  }
}

output workspaceId string = workspace.id
output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString
