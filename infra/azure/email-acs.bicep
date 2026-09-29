// Outgoing email for the FundFlow site through Azure Communication Services (ACS) Email over SMTP.
//
//   Email Communication Service  ->  an Azure-managed sender domain (<guid>.azurecomm.net, SPF/DKIM already set up)
//   Communication Service        ->  linked to that domain; owns the SMTP username
//   SMTP username                ->  ties smtp.azurecomm.net logins to an Entra application (its client secret is the password)
//   Role assignment              ->  the application may use *this* Communication Service and nothing else
//
// Things to know before relying on it (details in docs/deployment-azure.md):
//   * Azure-managed domains are capped at 5 emails/minute and 10 emails/hour per subscription, with no way to raise it
//     (a verified custom domain allows 30/minute and 100/hour, and can ask for more).
//   * Billed per email (about US$0.00025), so it is not part of the free tier; the resource group's budget alert covers it.
//   * Microsoft has announced the retirement of ACS Email on 2028-09-30. New resources can still be created today. The app
//     only speaks plain SMTP, so moving to another relay later is a settings change (infra/azure/set-smtp.ps1).
//
// infra/azure/setup-acs-email.ps1 creates the Entra application first, deploys this template, then creates the client secret
// and applies the SMTP settings to the web app.

targetScope = 'resourceGroup'

@description('Lowercase prefix for resource names.')
@minLength(3)
@maxLength(16)
param namePrefix string = 'fundflow'

@description('Same default as main.bicep, so the names line up with the web app and SQL server.')
param uniqueSuffix string = toLower(substring(uniqueString(subscription().id, resourceGroup().id), 0, 6))

@description('Where the email service keeps its data at rest. Must be the same for the email and communication services.')
param dataLocation string = 'India'

@description('SMTP username (free form; letters, digits and dashes).')
@minLength(3)
@maxLength(60)
param smtpUsername string = 'fundflow-smtp'

@description('Application (client) id of the Entra application whose client secret is the SMTP password.')
param entraApplicationId string

@description('Object id of that application\'s service principal; it receives the role on the Communication Service.')
param servicePrincipalObjectId string

@description('Tenant of the Entra application.')
param tenantId string = tenant().tenantId

var emailServiceName = 'email-${namePrefix}-${uniqueSuffix}'
var communicationServiceName = 'acs-${namePrefix}-${uniqueSuffix}'

// Built-in role "Communication and Email Service Owner": the role ACS documents for SMTP applications.
var communicationOwnerRoleId = '09976791-48a7-449e-bb21-39d1a415f350'

resource emailService 'Microsoft.Communication/emailServices@2025-09-01' = {
  name: emailServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
  }
}

resource azureManagedDomain 'Microsoft.Communication/emailServices/domains@2025-09-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled' // no tracking pixels in verification and reset emails
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2025-09-01' = {
  name: communicationServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
    linkedDomains: [
      azureManagedDomain.id
    ]
  }
}

resource smtpRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: communicationService
  name: guid(communicationService.id, servicePrincipalObjectId, communicationOwnerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', communicationOwnerRoleId)
    principalId: servicePrincipalObjectId
    principalType: 'ServicePrincipal'
  }
}

resource smtpUser 'Microsoft.Communication/communicationServices/smtpUsernames@2025-09-01' = {
  parent: communicationService
  name: '${smtpUsername}-login' // Azure rejects a resource whose name equals its username
  properties: {
    username: smtpUsername
    entraApplicationId: entraApplicationId
    tenantId: tenantId
  }
  dependsOn: [
    smtpRole
  ]
}

output emailServiceName string = emailService.name
output communicationServiceName string = communicationService.name
output smtpUsername string = smtpUser.properties.username
output senderDomain string = azureManagedDomain.properties.mailFromSenderDomain
