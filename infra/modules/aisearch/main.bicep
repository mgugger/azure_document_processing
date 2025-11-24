
param location string
param searchServiceName string

//param storageConnectionString string
//param inputContainerName string
//param outputContainerName string

resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
	name: searchServiceName
	location: location
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

// resource outputDataSource 'Microsoft.Search/searchServices/dataSources@2023-11-01' = {
// 	parent: searchService
// 	name: 'output-datasource'
// 	properties: {
// 		type: 'azureblob'
// 		credentials: {
// 			connectionString: storageConnectionString
// 		}
// 		container: {
// 			name: outputContainerName
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

output searchServiceName string = searchService.name
//output inputIndexName string = inputIndex.name
//output outputIndexName string = outputIndex.name
