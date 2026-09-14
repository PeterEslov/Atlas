// Project Atlas — Infrastructure as Code (Del 19)
//
// Codifies the Azure environment docs/AZURE_DEPLOYMENT.md built up by hand,
// one `az` command at a time, across Del 8 through Del 14: the App Service
// plan and Web App (section 1), the Azure SQL database (section 7), the
// Blob Storage account (section 8), the role assignments granting the
// deployed Web App's managed identity access to the existing Key Vault
// and existing Service Bus namespace (sections 3 and 9.1) — plus the two
// resources that were, until this Del, only ever a *plan* in that document
// and never actually provisioned: Application Insights (section 11) and
// Azure Cache for Redis (section 10).
//
// What this deliberately does NOT do:
//   - Create the resource group. Deploy this at resource-group scope
//     (`az deployment group create`, see below) against the `$RG` from
//     docs/AZURE_DEPLOYMENT.md section 1 (rg-projectatlas-dev-sc),
//     which you already created by hand there.
//   - Create the Key Vault, the SQL logical server, or the Service Bus
//     namespace — all three are existing resources this template only
//     references (`existing`) and grants access to. They're shared with
//     things outside this project (see docs/AZURE_DEPLOYMENT.md sections
//     3/7/9 for why each is reused rather than provisioned fresh), so
//     Del 19 only ever adds an RBAC role assignment against them, never
//     owns their lifecycle.
//   - Run EF Core migrations, seed data, or set the two secrets that
//     already exist in Key Vault (`Jwt--SigningKey`,
//     `ConnectionStrings--AtlasDb`) — Bicep provisions infrastructure, not
//     application/database state, the same division of labour
//     docs/ARCHITECTURE.md's Del 9 section already draws between `az sql
//     db create` and `dotnet ef database update`. The one exception is
//     `Redis--ConnectionString`, written below, because unlike the other
//     two secrets it genuinely doesn't exist anywhere yet before this Del
//     runs — there's no existing value this template could clobber.
//
// How to run it (you run this yourself — same reasoning as every `az`
// command in docs/AZURE_DEPLOYMENT.md: it's your subscription, your cost,
// your decision):
//
//   az bicep build --file infra/main.bicep          # syntax check only, no Azure calls — also what CI runs on every push
//   az deployment group what-if \
//     --resource-group rg-projectatlas-dev-sc \
//     --template-file infra/main.bicep \
//     --parameters infra/main.parameters.json        # preview what would change, nothing is deployed
//   az deployment group create \
//     --resource-group rg-projectatlas-dev-sc \
//     --template-file infra/main.bicep \
//     --parameters infra/main.parameters.json         # actually deploy
//
// infra/main.parameters.json has placeholder values for the four
// existing-resource names/resource-groups (Key Vault, SQL server, Service
// Bus namespace + their resource groups) and the storage account name —
// fill those in with your real values before running `what-if`/`create`.
//
// Safe to run against the App Service plan/Web App even though those
// already exist from Del 8: Bicep deployments are declarative (an ARM PUT
// under the hood), so running this the first time reconciles the existing
// resources against this template rather than recreating them — that's
// the actual point of Del 19, proving the environment can be redescribed
// from code, not just that new resources appear.

targetScope = 'resourceGroup'

@description('Azure region for every resource this template creates.')
param location string = 'swedencentral'

@description('Environment/stage suffix used throughout the naming convention <type>-<app>-<stage>-sc.')
@allowed([
  'dev'
])
param stage string = 'dev'

@description('Short app name used throughout the naming convention.')
param appName string = 'projectatlas'

@description('App Service plan SKU. F1 (free) where available, B1 otherwise — see docs/AZURE_DEPLOYMENT.md section 1.')
param appServicePlanSkuName string = 'F1'

@description('Web App name. Globally unique — becomes <name>.azurewebsites.net.')
param webAppName string = 'app-${appName}-${stage}-sc'

@description('Enable the Swagger UI surface on the deployed API — see the EnableSwaggerUi comment in Program.cs.')
param enableSwaggerUi bool = true

@description('Name of the existing Key Vault Atlas.Api reads secrets from (docs/AZURE_DEPLOYMENT.md section 3). No default — this is your own pre-existing vault.')
param keyVaultName string

@description('Resource group the existing Key Vault lives in.')
param keyVaultResourceGroup string

@description('Name of the existing Azure SQL logical server, without .database.windows.net (docs/AZURE_DEPLOYMENT.md section 7). No default — this is your own pre-existing server.')
param sqlServerName string

@description('Resource group the existing SQL server lives in.')
param sqlServerResourceGroup string

@description('Name of the new Atlas database to create on that server.')
param sqlDatabaseName string = 'sqldb-${appName}-${stage}-sc'

@description('Serverless SKU for the SQL database — GP_S_Gen5_2 is Gen5, max 2 vCores (docs/AZURE_DEPLOYMENT.md section 7.1).')
param sqlDatabaseSkuName string = 'GP_S_Gen5_2'

@description('Use Azure\'s monthly free SQL limit. Only one database per subscription can claim it — set false if another database already does.')
param sqlUseFreeLimit bool = true

@description('Name of the existing Service Bus namespace used for TicketAssigned events (docs/AZURE_DEPLOYMENT.md section 9). Defaults to the namespace local development already points at.')
param serviceBusNamespaceName string = 'nspl-sb-core-dev-sc'

@description('Resource group the existing Service Bus namespace lives in.')
param serviceBusResourceGroup string

@description('Globally unique storage account name for ticket attachments — lowercase letters/digits only, 3-24 chars (docs/AZURE_DEPLOYMENT.md section 8.1). No default: pick your own and check availability.')
param storageAccountName string

@description('Redis cache name. Globally unique — check availability before relying on the default.')
param redisName string = 'redis-${appName}-${stage}-sc'

@description('Application Insights component name.')
param appInsightsName string = 'appi-${appName}-${stage}-sc'

@description('Log Analytics workspace backing Application Insights.')
param logAnalyticsWorkspaceName string = 'la-${appName}-${stage}-sc'

var appServicePlanName = 'plan-${appName}-${stage}-sc'

// --- Resources that live entirely in this resource group ---

module appServicePlan 'modules/appServicePlan.bicep' = {
  name: 'appServicePlan'
  params: {
    name: appServicePlanName
    location: location
    skuName: appServicePlanSkuName
  }
}

module appInsights 'modules/applicationInsights.bicep' = {
  name: 'appInsights'
  params: {
    appInsightsName: appInsightsName
    logAnalyticsWorkspaceName: logAnalyticsWorkspaceName
    location: location
  }
}

module storage 'modules/storageAccount.bicep' = {
  name: 'storage'
  params: {
    name: storageAccountName
    location: location
  }
}

module redis 'modules/redisCache.bicep' = {
  name: 'redis'
  params: {
    name: redisName
    location: location
  }
}

module webApp 'modules/webApp.bicep' = {
  name: 'webApp'
  params: {
    name: webAppName
    location: location
    appServicePlanId: appServicePlan.outputs.id
    keyVaultName: keyVaultName
    blobStorageAccountUrl: storage.outputs.blobEndpoint
    serviceBusNamespaceHostname: '${serviceBusNamespaceName}.servicebus.windows.net'
    appInsightsConnectionString: appInsights.outputs.connectionString
    enableSwaggerUi: enableSwaggerUi
  }
}

module storageRoleAssignment 'modules/storageRoleAssignment.bicep' = {
  name: 'storageRoleAssignment'
  params: {
    storageAccountName: storage.outputs.name
    principalId: webApp.outputs.principalId
  }
}

// --- Resources that live in a different resource group, deployed cross-scope ---

module sqlDatabase 'modules/sqlDatabase.bicep' = {
  name: 'sqlDatabase'
  scope: resourceGroup(sqlServerResourceGroup)
  params: {
    sqlServerName: sqlServerName
    databaseName: sqlDatabaseName
    location: location
    skuName: sqlDatabaseSkuName
    useFreeLimit: sqlUseFreeLimit
  }
}

module keyVaultRoleAssignment 'modules/keyVaultRoleAssignment.bicep' = {
  name: 'keyVaultRoleAssignment'
  scope: resourceGroup(keyVaultResourceGroup)
  params: {
    keyVaultName: keyVaultName
    principalId: webApp.outputs.principalId
  }
}

module serviceBusRoleAssignment 'modules/serviceBusRoleAssignment.bicep' = {
  name: 'serviceBusRoleAssignment'
  scope: resourceGroup(serviceBusResourceGroup)
  params: {
    serviceBusNamespaceName: serviceBusNamespaceName
    principalId: webApp.outputs.principalId
  }
}

// The one genuinely new secret this Del writes — see the file header for
// why Jwt--SigningKey and ConnectionStrings--AtlasDb are deliberately left
// untouched.
//
// A known limitation worth being upfront about: redis.outputs.primaryKey
// passes the Redis access key between modules as a plain (non-@secure())
// output, because Bicep module outputs can't be marked @secure() the way
// parameters can — the value ends up recorded in this deployment's
// history in the Azure portal/CLI (`az deployment group show`), readable
// by anyone with read access to the resource group's deployment history,
// not just Key Vault Secrets User. Same category of gap as the reused SQL
// admin login in docs/ARCHITECTURE.md's known-simplifications list — a
// real difference from a production setup, not something this Del solves.
// Rotating the Redis key after deployment (`az redis regenerate-keys`) and
// clearing old deployment history are the practical mitigations.
module redisSecret 'modules/keyVaultSecret.bicep' = {
  name: 'redisSecret'
  scope: resourceGroup(keyVaultResourceGroup)
  params: {
    keyVaultName: keyVaultName
    secretName: 'Redis--ConnectionString'
    secretValue: '${redis.outputs.hostName}:${redis.outputs.sslPort},password=${redis.outputs.primaryKey},ssl=True,abortConnect=False'
  }
}

output webAppName string = webApp.outputs.name
output webAppDefaultHostName string = webApp.outputs.defaultHostName
output sqlDatabaseName string = sqlDatabase.outputs.name
output storageAccountName string = storage.outputs.name
output appInsightsName string = appInsights.outputs.name
output redisHostName string = redis.outputs.hostName
