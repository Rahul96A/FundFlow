// Optional cost guard for the FundFlow resource group: emails you when actual spend passes 50% and 100% of a small monthly
// budget. Everything in main.bicep is free, so any spend at all means something changed (for example a free allowance was
// exceeded with "continue using database for additional charges", or a resource was added by hand).
//
// A budget only *notifies*: Azure evaluates it a few times a day and never stops resources. Deployed by deploy.ps1 when
// -BudgetEmail is given.

targetScope = 'resourceGroup'

@description('Budget name.')
param budgetName string = 'fundflow-free-guard'

@description('Monthly amount in the subscription currency.')
@minValue(1)
param amount int = 5

@description('Where the alerts go.')
param contactEmail string

@description('First day of the current month (a budget period must start on the 1st).')
param startDate string = '${utcNow('yyyy-MM')}-01T00:00:00Z'

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: budgetName
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      actual50: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: [ contactEmail ]
      }
      actual100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [ contactEmail ]
      }
    }
  }
}
