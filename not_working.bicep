param sites_xkr3wunb46plk_inbound_process_name string = 'xkr3wunb46plk-inbound-process'
param serverfarms_xkr3wunb46plk_plan_externalid string = '/subscriptions/1da3d717-dc11-47ec-86b4-040c260ad30b/resourceGroups/rg-mdgcorpdocintel/providers/Microsoft.Web/serverfarms/xkr3wunb46plk-plan'

resource sites_xkr3wunb46plk_inbound_process_name_resource 'Microsoft.Web/sites@2024-11-01' = {
  name: sites_xkr3wunb46plk_inbound_process_name
  location: 'Sweden Central'
  tags: {
    'azd-service-name': 'function_inbound_process'
    'hidden-link: /app-insights-resource-id': '/subscriptions/1da3d717-dc11-47ec-86b4-040c260ad30b/resourceGroups/rg-mdgcorpdocintel/providers/microsoft.insights/components/xkr3wunb46plk-ai'
  }
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    enabled: true
    hostNameSslStates: [
      {
        name: '${sites_xkr3wunb46plk_inbound_process_name}.azurewebsites.net'
        sslState: 'Disabled'
        hostType: 'Standard'
      }
      {
        name: '${sites_xkr3wunb46plk_inbound_process_name}.scm.azurewebsites.net'
        sslState: 'Disabled'
        hostType: 'Repository'
      }
    ]
    serverFarmId: serverfarms_xkr3wunb46plk_plan_externalid
    reserved: true
    isXenon: false
    hyperV: false
    dnsConfiguration: {}
    outboundVnetRouting: {
      allTraffic: false
      applicationTraffic: false
      contentShareTraffic: false
      imagePullTraffic: false
      backupRestoreTraffic: false
    }
    siteConfig: {
      numberOfWorkers: 1
      acrUseManagedIdentityCreds: false
      alwaysOn: false
      http20Enabled: false
      functionAppScaleLimit: 100
      minimumElasticInstanceCount: 0
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobcontainer'
          value: 'https://b56rqdx2o6m4a.blob.core.windows.net/deployments'
          authentication: {
            type: 'systemassignedidentity'
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '9.0'
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
    }
    scmSiteAlsoStopped: false
    clientAffinityEnabled: false
    clientAffinityProxyEnabled: false
    clientCertEnabled: false
    clientCertMode: 'Required'
    hostNamesDisabled: false
    ipMode: 'IPv4'
    customDomainVerificationId: '467866CB8FCF1D16007ABBA99BD8F76C38112D008D7252D044C5520B61D47A07'
    containerSize: 1536
    dailyMemoryTimeQuota: 0
    httpsOnly: true
    endToEndEncryptionEnabled: false
    redundancyMode: 'None'
    publicNetworkAccess: 'Enabled'
    storageAccountRequired: false
    keyVaultReferenceIdentity: 'SystemAssigned'
  }
}

resource sites_xkr3wunb46plk_inbound_process_name_ftp 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: sites_xkr3wunb46plk_inbound_process_name_resource
  name: 'ftp'
  location: 'Sweden Central'
  tags: {
    'azd-service-name': 'function_inbound_process'
    'hidden-link: /app-insights-resource-id': '/subscriptions/1da3d717-dc11-47ec-86b4-040c260ad30b/resourceGroups/rg-mdgcorpdocintel/providers/microsoft.insights/components/xkr3wunb46plk-ai'
  }
  properties: {
    allow: false
  }
}

resource sites_xkr3wunb46plk_inbound_process_name_scm 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: sites_xkr3wunb46plk_inbound_process_name_resource
  name: 'scm'
  location: 'Sweden Central'
  tags: {
    'azd-service-name': 'function_inbound_process'
    'hidden-link: /app-insights-resource-id': '/subscriptions/1da3d717-dc11-47ec-86b4-040c260ad30b/resourceGroups/rg-mdgcorpdocintel/providers/microsoft.insights/components/xkr3wunb46plk-ai'
  }
  properties: {
    allow: false
  }
}

resource sites_xkr3wunb46plk_inbound_process_name_web 'Microsoft.Web/sites/config@2024-11-01' = {
  parent: sites_xkr3wunb46plk_inbound_process_name_resource
  name: 'web'
  location: 'Sweden Central'
  tags: {
    'azd-service-name': 'function_inbound_process'
    'hidden-link: /app-insights-resource-id': '/subscriptions/1da3d717-dc11-47ec-86b4-040c260ad30b/resourceGroups/rg-mdgcorpdocintel/providers/microsoft.insights/components/xkr3wunb46plk-ai'
  }
  properties: {
    numberOfWorkers: 1
    defaultDocuments: [
      'Default.htm'
      'Default.html'
      'Default.asp'
      'index.htm'
      'index.html'
      'iisstart.htm'
      'default.aspx'
      'index.php'
    ]
    netFrameworkVersion: 'v4.0'
    requestTracingEnabled: false
    remoteDebuggingEnabled: false
    httpLoggingEnabled: false
    acrUseManagedIdentityCreds: false
    logsDirectorySizeLimit: 35
    detailedErrorLoggingEnabled: false
    publishingUsername: 'REDACTED'
    scmType: 'None'
    use32BitWorkerProcess: false
    webSocketsEnabled: false
    alwaysOn: false
    managedPipelineMode: 'Integrated'
    virtualApplications: [
      {
        virtualPath: '/'
        physicalPath: 'site\\wwwroot'
        preloadEnabled: false
      }
    ]
    loadBalancing: 'LeastRequests'
    experiments: {
      rampUpRules: []
    }
    autoHealEnabled: false
    vnetRouteAllEnabled: false
    vnetPrivatePortsCount: 0
    localMySqlEnabled: false
    managedServiceIdentityId: 15085
    ipSecurityRestrictions: [
      {
        ipAddress: 'Any'
        action: 'Allow'
        priority: 2147483647
        name: 'Allow all'
        description: 'Allow all access'
      }
    ]
    scmIpSecurityRestrictions: [
      {
        ipAddress: 'Any'
        action: 'Allow'
        priority: 2147483647
        name: 'Allow all'
        description: 'Allow all access'
      }
    ]
    scmIpSecurityRestrictionsUseMain: false
    http20Enabled: false
    minTlsVersion: '1.2'
    scmMinTlsVersion: '1.2'
    ftpsState: 'FtpsOnly'
    preWarmedInstanceCount: 0
    functionAppScaleLimit: 100
    functionsRuntimeScaleMonitoringEnabled: false
    minimumElasticInstanceCount: 0
    azureStorageAccounts: {}
    http20ProxyFlag: 0
  }
}

resource sites_xkr3wunb46plk_inbound_process_name_08a9bd05_3814_4d04_ae7d_6b2eca8e4a50 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_xkr3wunb46plk_inbound_process_name_resource
  name: '08a9bd05-3814-4d04-ae7d-6b2eca8e4a50'
  location: 'Sweden Central'
  properties: {
    status: 4
    deployer: 'LegionOneDeploy'
    start_time: '2025-08-21T21:07:06.8636137Z'
    end_time: '2025-08-21T21:08:07.5388003Z'
    active: false
  }
}

resource sites_xkr3wunb46plk_inbound_process_name_6eff86dc_ed9b_4a5b_a8a0_d02dd1d008da 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_xkr3wunb46plk_inbound_process_name_resource
  name: '6eff86dc-ed9b-4a5b-a8a0-d02dd1d008da'
  location: 'Sweden Central'
  properties: {
    status: 3
    deployer: 'LegionOneDeploy'
    start_time: '2025-08-21T21:04:49.5981303Z'
    end_time: '2025-08-21T21:04:49.9828196Z'
    active: false
  }
}

resource sites_xkr3wunb46plk_inbound_process_name_bddef34f_20d6_4f18_85af_c7537e34c26c 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_xkr3wunb46plk_inbound_process_name_resource
  name: 'bddef34f-20d6-4f18-85af-c7537e34c26c'
  location: 'Sweden Central'
  properties: {
    status: 4
    deployer: 'ms-azuretools-vscode'
    start_time: '2025-08-21T19:57:43.1540615Z'
    end_time: '2025-08-21T19:59:04.2045466Z'
    active: false
  }
}

resource sites_xkr3wunb46plk_inbound_process_name_de48d500_3812_4ef3_b243_cf3e85b897ba 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_xkr3wunb46plk_inbound_process_name_resource
  name: 'de48d500-3812-4ef3-b243-cf3e85b897ba'
  location: 'Sweden Central'
  properties: {
    status: 4
    deployer: 'LegionOneDeploy'
    start_time: '2025-08-21T21:39:44.6814214Z'
    end_time: '2025-08-21T21:40:45.1716743Z'
    active: true
  }
}

resource sites_xkr3wunb46plk_inbound_process_name_WarmUp 'Microsoft.Web/sites/functions@2024-11-01' = {
  parent: sites_xkr3wunb46plk_inbound_process_name_resource
  name: 'WarmUp'
  location: 'Sweden Central'
  properties: {
    script_href: 'https://xkr3wunb46plk-inbound-process.azurewebsites.net/admin/vfs/tmp/functions/standby/wwwroot/WarmUp/run.csx'
    test_data_href: 'https://xkr3wunb46plk-inbound-process.azurewebsites.net/admin/vfs/tmp/FunctionsData/WarmUp.dat'
    href: 'https://xkr3wunb46plk-inbound-process.azurewebsites.net/admin/functions/WarmUp'
    config: {
      name: 'WarmUp'
      scriptFile: '/tmp/functions/standby/wwwroot/WarmUp/run.csx'
      language: 'CSharp'
      functionDirectory: '/tmp/functions/standby/wwwroot/WarmUp'
      bindings: [
        {
          type: 'httpTrigger'
          authLevel: 'anonymous'
          name: 'req'
          direction: 'in'
          methods: [
            'get'
            'post'
          ]
          route: '{x:regex(^(warmup|csharphttpwarmup)$)}'
        }
        {
          type: 'http'
          name: '$return'
          direction: 'out'
        }
      ]
    }
    invoke_url_template: 'https://xkr3wunb46plk-inbound-process.azurewebsites.net/api/{x:regex(^(warmup|csharphttpwarmup)$)}'
    language: 'CSharp'
    isDisabled: false
  }
}

resource sites_xkr3wunb46plk_inbound_process_name_sites_xkr3wunb46plk_inbound_process_name_azurewebsites_net 'Microsoft.Web/sites/hostNameBindings@2024-11-01' = {
  parent: sites_xkr3wunb46plk_inbound_process_name_resource
  name: '${sites_xkr3wunb46plk_inbound_process_name}.azurewebsites.net'
  location: 'Sweden Central'
  properties: {
    siteName: 'xkr3wunb46plk-inbound-process'
    hostNameType: 'Verified'
  }
}
