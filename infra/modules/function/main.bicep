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

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  tags: tags
  properties: {
    serverFarmId: appServicePlanId
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}/deployments'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '9.0'
      }
    }
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
          value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}/'
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: 'https://${storageAccountName}.queue.${environment().suffixes.storage}/'
        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri'
          value: 'https://${storageAccountName}.table.${environment().suffixes.storage}/'
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
          name: 'SCALE_CONTROLLER_LOGGING_ENABLED'
          value: '1'
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
        {
          name: 'DEFAULT_WORKFLOW'
          value: 'docintelligence,pii,translation'
        }
        {
          name: 'WORKFLOW_QUEUE_DOCINTELLIGENCE'
          value: 'workflow-docintelligence'
        }
        {
          name: 'WORKFLOW_QUEUE_PII'
          value: 'workflow-pii'
        }
        {
          name: 'WORKFLOW_QUEUE_TRANSLATION'
          value: 'workflow-translation'
        }
        {
          name: 'WORKFLOW_QUEUE_AIVISION'
          value: 'workflow-aivision'
        }
        {
          name: 'WORKFLOW_QUEUE_GPTVISION'
          value: 'workflow-gptvision'
        }
        {
          name: 'WORKFLOW_QUEUE_PDFIMAGES'
          value: 'workflow-pdfimages'
        }
        {
          name: 'WORKFLOW_ALERT_QUEUE'
          value: 'workflow-alerts'
        }
      ]
    }
    httpsOnly: true
    reserved: true
  }
  identity: {
    type: 'SystemAssigned'
  }
}

output functionAppName string = functionApp.name
output principalId string = functionApp.identity.principalId
