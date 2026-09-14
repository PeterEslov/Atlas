// A new, empty database on Peter's existing Azure SQL logical server —
// same "reuse the resource, keep the project's data to itself" idea
// docs/AZURE_DEPLOYMENT.md section 7 already used by hand for Del 9. The
// server itself is declared `existing`: this module only ever creates the
// database, never the server, and is deployed scoped to whichever resource
// group that server actually lives in (see the `scope:` on the module call
// in main.bicep) — the existing server may not be in the same resource
// group as everything else Del 19 provisions.
//
// Serverless General Purpose, Gen5, 2 vCores max, auto-pause after 60
// minutes idle, using Azure's monthly free limit — identical settings to
// the `az sql db create` call in section 7.1, just declared instead of
// scripted.
//
// What this module deliberately does NOT do: run `dotnet ef database
// update` (Bicep provisions infrastructure, not schema — EF Core owns the
// schema, same division of responsibility docs/ARCHITECTURE.md's Del 9
// section already draws), and it does not touch the server's admin
// credentials or firewall rules — those stay exactly as section 7.2/7.3
// already set them up.

@description('Name of the existing Azure SQL logical server (without .database.windows.net).')
param sqlServerName string

@description('Name of the new database to create on that server.')
param databaseName string

@description('Azure region — should match the existing server\'s region.')
param location string

@description('Serverless General Purpose SKU name, e.g. GP_S_Gen5_2 (Gen5, max 2 vCores).')
param skuName string = 'GP_S_Gen5_2'

@description('Use Azure\'s free monthly limit (100,000 vCore-seconds, 32 GB storage). Only one database per subscription can use it — set to false if another database already claims it.')
param useFreeLimit bool = true

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' existing = {
  name: sqlServerName
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: skuName
    tier: 'GeneralPurpose'
  }
  properties: {
    autoPauseDelay: 60
    useFreeLimit: useFreeLimit
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}

output name string = sqlDatabase.name
