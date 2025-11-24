param piiDetectionEndpoint string
param piiDetectionName string
param appInsightsInstrumentationKey string
param appInsightsConnectionString string
param location string
param functionAppName string
param storageAccountName string
param aiVisionEndpoint string
param aiLanguageEndpoint string
param aiTranslatorEndpoint string
param appServicePlanId string
param tags object
param documentIntelligenceEndpoint string
param gptVisionEndpoint string
param gptVisionDeploymentName string

resource functionApp 'Microsoft.Web/sites@2022-09-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  tags: tags
  properties: {
    serverFarmId: appServicePlanId
    siteConfig: {
      cors: {
        allowedOrigins: [
          'https://portal.azure.com'
        ]
        supportCredentials: false
      }
      appSettings: [
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsightsInstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'GPT4_VISION_ENDPOINT'
          value: gptVisionEndpoint
        }
        {
          name: 'GPT4_VISION_DEPLOYMENT_NAME'
          value: split(gptVisionDeploymentName, '/')[1]
        }
        {
          name: 'PII_DETECTION_ENDPOINT'
          value: piiDetectionEndpoint
        }
        {
          name: 'PII_DETECTION_NAME'
          value: piiDetectionName
        }
        {
          name: 'AI_VISION_ENDPOINT'
          value: aiVisionEndpoint
        }
        {
          name: 'AI_LANGUAGE_ENDPOINT'
          value: aiLanguageEndpoint
        }
        {
          name: 'AI_TRANSLATOR_ENDPOINT'
          value: aiTranslatorEndpoint
        }
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: 'https://${storageAccountName}.blob.core.windows.net/'
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: 'https://${storageAccountName}.queue.core.windows.net/'
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'DOCUMENT_INTELLIGENCE_ENDPOINT'
          value: documentIntelligenceEndpoint
        }
        {
          name: 'STORAGE_ACCOUNT_NAME'
          value: storageAccountName
        }
        {
          name: 'OPERATION_QUEUE_NAME'
          value: 'documentintelligence-events'
        }
      ]
    }
    functionAppConfig: {
      runtime: {
        name: 'dotnet-isolated'
        version: '9.0'
      }
      scaleAndConcurrency: {
        instanceMemoryMB: 2048
        maximumInstanceCount: 100
      }
      deployment: {
        storage: {
          type: 'blobContainer'
          value: 'https://${storageAccountName}.blob.core.windows.net/deployments'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
    }
    httpsOnly: true
  }
  identity: {
    type: 'SystemAssigned'
  }
}

output functionAppName string = functionApp.name
output principalId string = functionApp.identity.principalId
