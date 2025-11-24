param location string
param logicAppName string

resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: logicAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      triggers: {
        manual: {
          type: 'Request'
          kind: 'Http'
          inputs: {
            schema: {}
          }
        }
      }
      actions: {}
      outputs: {}
    }
    parameters: {}
    state: 'Enabled'
  }
}

output logicAppName string = logicApp.name
output logicAppId string = logicApp.id
output principalId string = logicApp.identity.principalId
