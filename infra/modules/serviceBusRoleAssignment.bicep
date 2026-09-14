// Grants the Web App's managed identity permission to publish
// (Send, never receive/manage) on an *existing* Service Bus namespace —
// the role docs/AZURE_DEPLOYMENT.md section 9.1 already assigned by hand
// for Del 12's deployed side, just declared instead of run as
// `az role assignment create`.
//
// "Data Sender", deliberately not "Data Owner" (the role Peter's own
// identity has locally, so it can both send and receive during
// development, see docs/ARCHITECTURE.md's Del 12 section): the deployed
// Atlas.Api only ever publishes, so it gets no more than that —
// least-privilege, the same call section 9.1 already made.
//
// The namespace itself is `existing`, never created here (it's shared —
// docs/AZURE_DEPLOYMENT.md section 9 explains why Atlas reuses the same
// namespace local development already talks to, rather than provisioning
// its own) — deployed scoped to whichever resource group it actually
// lives in (see the `scope:` on this module's call in main.bicep).

@description('Name of the existing Service Bus namespace.')
param serviceBusNamespaceName string

@description('principalId of the identity to grant Send access to (the Web App\'s managed identity).')
param principalId string

// Built-in role definition GUID for "Azure Service Bus Data Sender" —
// stable across all Azure tenants
// (https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/integration#azure-service-bus-data-sender).
var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, principalId, serviceBusDataSenderRoleId)
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
