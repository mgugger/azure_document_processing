
param location string
param searchServiceName string
param storageAccountName string
param outputContainerName string = 'output'
@description('Name used for the data source that reads processed blobs')
param outputDataSourceName string = 'processed-output-datasource'
@description('Name used for the index that stores processed document metadata')
param outputIndexName string = 'processed-output-index'
@description('Name used for the indexer wiring the output container to the search index')
param outputIndexerName string = 'processed-output-indexer'

var storageBlobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
  name: storageAccountName
}

resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
	name: searchServiceName
	location: location
	identity: {
		type: 'SystemAssigned'
	}
	sku: {
		name: 'basic'
	}
	properties: {
		hostingMode: 'default'
		partitionCount: 1
		replicaCount: 1
		publicNetworkAccess: 'enabled'
	}
}

// resource inputDataSource 'Microsoft.Search/searchServices/dataSources@2023-11-01' = {
// 	parent: searchService
// 	name: 'input-datasource'
// 	properties: {
// 		type: 'azureblob'
// 		credentials: {
// 			connectionString: storageConnectionString
// 		}
// 		container: {
// 			name: inputContainerName
// 		}
// 		dataChangeDetectionPolicy: {
// 			'@odata.type': '#Microsoft.Azure.Search.HighWaterMarkChangeDetectionPolicy'
// 			highWaterMarkColumnName: 'LastModified'
// 		}
// 		dataDeletionDetectionPolicy: null
// 	}

// }

// resource inputIndex 'Microsoft.Search/searchServices/indexes@2023-11-01' = {
// 	parent: searchService
// 	name: 'input-index'
// 	properties: {
// 		fields: [
// 			{
// 				name: 'id'
// 				type: 'Edm.String'
// 				key: true
// 				searchable: false
// 				filterable: false
// 				sortable: false
// 				facetable: false
// 				retrievable: true
// 			}
// 			{
// 				name: 'content'
// 				type: 'Edm.String'
// 				searchable: true
// 				filterable: false
// 				sortable: false
// 				facetable: false
// 				retrievable: true
// 			}
// 			{
// 				name: 'metadata_storage_last_modified'
// 				type: 'Edm.DateTimeOffset'
// 				filterable: true
// 				sortable: true
// 				facetable: false
// 				retrievable: true
// 			}
// 		]
// 	}

// }

// resource outputIndex 'Microsoft.Search/searchServices/indexes@2023-11-01' = {
// 	parent: searchService
// 	name: 'output-index'
// 	properties: {
// 		fields: [
// 			{
// 				name: 'id'
// 				type: 'Edm.String'
// 				key: true
// 				searchable: false
// 				filterable: false
// 				sortable: false
// 				facetable: false
// 				retrievable: true
// 			}
// 			{
// 				name: 'content'
// 				type: 'Edm.String'
// 				searchable: true
// 				filterable: false
// 				sortable: false
// 				facetable: false
// 				retrievable: true
// 			}
// 			{
// 				name: 'metadata_storage_last_modified'
// 				type: 'Edm.DateTimeOffset'
// 				filterable: true
// 				sortable: true
// 				facetable: false
// 				retrievable: true
// 			}
// 		]
// 	}

// }

resource searchServiceBlobReaderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
	name: guid(storageAccount.id, searchService.name, storageBlobDataReaderRoleId)
	scope: storageAccount
	properties: {
		principalId: searchService.identity.principalId
		roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReaderRoleId)
		principalType: 'ServicePrincipal'
	}
}

output searchServiceName string = searchService.name
output processedOutputDataSourceName string = outputDataSourceName
output processedOutputIndexName string = outputIndexName
output processedOutputIndexerName string = outputIndexerName
output processedOutputContainerName string = outputContainerName
output searchServicePrincipalId string = searchService.identity.principalId
