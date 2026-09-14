// The Atlas.Api Web App itself — Linux, .NET 10, system-assigned managed
// identity. This is the resource everything else in infra/ grants access
// to: Key Vault (keyVaultRoleAssignment.bicep), the storage account
// (role assignment inline in main.bicep), and the existing Service Bus
// namespace (serviceBusRoleAssignment.bicep) all target this identity's
// principalId, exactly the same "no stored secret, the identity IS the
// credential" pattern docs/AZURE_DEPLOYMENT.md section 3 already
// established by hand for Del 8.
//
// appSettings intentionally only carries the non-secret values
// (docs/AZURE_DEPLOYMENT.md sections 2/3/8.3/9.1/11.2 all make the same
// call: a value like a Key Vault *name* or a Service Bus *namespace
// hostname* isn't secret on its own — no access follows from knowing it
// without an identity Azure already trusts). The two real secrets
// (`Jwt--SigningKey`, `ConnectionStrings--AtlasDb`) live in Key Vault and
// were set by hand in Del 8/9 — this module deliberately does not
// overwrite them, so re-running this deployment can never wipe a secret
// that's already correctly configured.

@description('Name of the Web App. Must be globally unique (part of <name>.azurewebsites.net).')
param name string

@description('Azure region for the Web App.')
param location string

@description('Resource ID of the App Service plan to run on.')
param appServicePlanId string

@description('Name of the existing Key Vault Atlas.Api reads secrets from.')
param keyVaultName string

@description('Fully qualified hostname of the Blob Storage account for attachments.')
param blobStorageAccountUrl string

@description('Fully qualified namespace hostname of the Service Bus namespace used for TicketAssigned events.')
param serviceBusNamespaceHostname string

@description('Application Insights connection string. Empty disables the Serilog sink (see Program.cs).')
param appInsightsConnectionString string

@description('Enable the Swagger UI surface. A deliberate demo-project choice — see the EnableSwaggerUi comment in Program.cs.')
param enableSwaggerUi bool = true

@description('Keep the app loaded instead of unloading it after 20 minutes idle. Defaults to false because Azure\'s Free (F1) tier — this project\'s default App Service plan SKU, see appServicePlan.bicep — does not support Always On at all; a `create` with this true on F1 fails outright. Only set true once running on Basic (B1) or higher, where it\'s worth the (small) cost to avoid cold starts.')
param alwaysOn bool = false

resource webApp 'Microsoft.Web/sites@2023-01-01' = {
  name: name
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: alwaysOn
      appSettings: [
        { name: 'Jwt__Issuer', value: 'ProjectAtlas' }
        { name: 'Jwt__Audience', value: 'ProjectAtlas.Api' }
        { name: 'Jwt__ExpiryMinutes', value: '60' }
        { name: 'EnableSwaggerUi', value: string(enableSwaggerUi) }
        { name: 'KeyVault__Name', value: keyVaultName }
        { name: 'BlobStorage__AccountUrl', value: blobStorageAccountUrl }
        { name: 'ServiceBus__FullyQualifiedNamespace', value: serviceBusNamespaceHostname }
        { name: 'ApplicationInsights__ConnectionString', value: appInsightsConnectionString }
      ]
    }
  }
}

output name string = webApp.name
output principalId string = webApp.identity.principalId
output defaultHostName string = webApp.properties.defaultHostName
