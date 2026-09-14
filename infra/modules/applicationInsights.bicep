// Application Insights for Atlas.Api — the Del 14 resource that was, until
// Del 19, only ever created by hand (docs/AZURE_DEPLOYMENT.md section 11).
// Workspace-based (not the older "classic" standalone mode Azure retired
// new creation of a while back), so a Log Analytics workspace is created
// alongside it and wired up as the required WorkspaceResourceId.
//
// No code change needed for this to start working — Program.cs's
// UseSerilog block already reads ApplicationInsights:ConnectionString from
// config and only attaches the sink when the value is non-empty (see
// docs/ARCHITECTURE.md's Del 14 section). This module's output is exactly
// the value webApp.bicep wires into the ApplicationInsights__ConnectionString
// app setting in main.bicep.

@description('Name of the Application Insights component.')
param appInsightsName string

@description('Name of the Log Analytics workspace backing it.')
param logAnalyticsWorkspaceName string

@description('Azure region for both resources.')
param location string

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30 // portfolio project, not a compliance archive — 30 is the cheapest tier above the free trial default
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

output connectionString string = appInsights.properties.ConnectionString
output name string = appInsights.name
