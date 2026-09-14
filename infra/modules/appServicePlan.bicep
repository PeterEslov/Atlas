// Linux App Service plan for Atlas.Api.
//
// This mirrors exactly what `az appservice plan create` already did by hand
// in docs/AZURE_DEPLOYMENT.md section 1 (Del 8) — F1 (free) where the
// region/OS combination allows it, falling back to B1 if not. Bicep can't
// make that runtime decision for you (F1 availability varies by region and
// changes over time), so `skuName` is a parameter with F1 as the default;
// if a deployment fails because F1 isn't available in your region right
// now, redeploy with `--parameters appServicePlanSkuName=B1`.

@description('Name of the App Service plan.')
param name string

@description('Azure region for the plan.')
param location string

@description('SKU name, e.g. F1 (free) or B1 (basic, ~$13/month). F1 may not be available in every region.')
param skuName string = 'F1'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: name
  location: location
  sku: {
    name: skuName
  }
  kind: 'linux'
  properties: {
    reserved: true // required for Linux plans
  }
}

output id string = appServicePlan.id
output name string = appServicePlan.name
