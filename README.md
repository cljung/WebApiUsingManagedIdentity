# WebApiUsingManagedIdentity

There is a [blog post](https://blog.redbaronofazure.com/?p=8113) explaining the high level details of this sample code.

## Configuration via Powershell

There are powershell configuration scripts in the [scripts](scripts) folder. Run them in the following order.

| Step | Script | Details |
|------|--------|--------|
| 1. | [Create-AppRegs.ps1](scripts/Create-AppRegs.ps1) | Creates the Entra app registrations for Backend WebApi and Frontend WebApp. It outputs JSON at end that can be copied into the appsettings.json files |
| 2. | [grant-MI-permissions.ps1](scripts/grant-MI-permissions.ps1) | Adds permissions to the Managed Identity service principal |
| 3. | [test.script.ps1](scripts/test.script.ps1) | Test script that calls the WebApi with an app token and a user token |

Important things to notice:

- After the app registration script, you must grant admin consent to the WebApp in the Entra portal.

### Create-AppRegs.ps1 output

```Powershell
.\Create-AppRegs.ps1

Creating App Registration: WebApiUsingManagedIdentity-BackEnd-test...
Updating Application with Identifier URI...
Adding Client Secret Credential...
Creating App Service Principal...
Assigning user johndoe@your-domain.com as owner...
Creating Front-End App Registration: WebApiUsingManagedIdentity-FrontEnd-test...
Generating Client Secret...
Creating App Service Principal...
Assigning user johndoe@your-domain.com as owner...

appsettings.json details:

{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Entra": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "your-domain.com",
    "TenantID": "11111111-2222-3333-4444-555555555555",
    "ClientId": "22222222-3333-4444-5555-666666666666",
    "ClientSecret": "your-backend-webapi-secret",
    "Audience": "api://33333333-4444-5555-6666-777777777777",
    "Graph": {
      "AuthMode": "ClientCreds",
      "ClientId": "33333333-4444-5555-6666-777777777777",
      "ClientSecret": "your-backend-webapi-secret"
    }
  },
  "KeyVault": {
    "AuthMode": "ClientCreds",
    "VaultUri": "https://your-kv-name.vault.azure.net/",
    "SecretName": "33333333-4444-5555-6666-777777777777"
  }
}
*** TODO ***
1) Create a KeyVault secret with the name 33333333-4444-5555-6666-777777777777 and value your-backend-webapi-secret
2) Grant Get Secret access to app 33333333-4444-5555-6666-777777777777 in KeyVault
3) Update appsettings.json with your KeyVaultUri
4) Test client credentials work
5) Switch to managed identity
```

### Powershell grant-MI-permissions.ps1

Just change the line below with the Azure App Services Managed Identity objectId and run the script.

```Powershell
$ManagedIdentityObjectId = "YOUR_FUNCTION_MANAGED_IDENTITY_OBJECT_ID"
```

## Configuration manually in the Entra portal

### App registrations

1. Create an app registration for the **WebApi** with Redirect Uri `https://localhost`
	1. In `Expose an API`¨, add a scope `access_as_user`
	1. In `App roles`, add a role `Api.Read`.
	1. Create a client secret
1. Create an app registration for the **WebApp** with Web Redirect Uri `https://localhost`
	1. In 'API Permissions', select `Add a permission` > My APIs > your WebApi > Delegated > access_as_user
	1. In 'API Permissions', select `Add a permission` > My APIs > your WebApi > Application > Api.Read
	1. Grant admin consent
	1. Create a client secret
    1. Go to `Authentication`, click `+ Add Redirect Uri`, select `Mobile & Desktop apps`, select `https://login.microsoftonline.com/common/oauth2/nativeclient`
    1. Go to `Authentication` > `Settings`, enable `Allow public client flow` and select both `Access tokens` and `ID tokens`
1. Update both appsettings.json files with the details

### Managed Identity configuration

There is no user interface in Entra Admin Center, so you have to do this via Powershell.

## Deploy to Azure App Services

1. Deploy the solution to Azure App Services
1. Goto Settings > Environment variables and click `Advanced edit` and paste in the below JSON. Add your details
1. Goto Settings > Managed Identity and enable it

```JSON
[
  {
    "name": "Entra__Audience",
    "value": "api://your-backend-webapi-appid",
    "slotSetting": false
  },
  {
    "name": "Entra__ClientID",
    "value": "your-webapp-fronend-appid",
    "slotSetting": false
  },
  {
    "name": "Entra__ClientSecret",
    "value": "your-webapp-frontend-secret",
    "slotSetting": false
  },
  {
    "name": "Entra__Domain",
    "value": "your-domain.com",
    "slotSetting": false
  },
  {
    "name": "Entra__Graph__AuthMode",
    "value": "ManagedIdentity",
    "slotSetting": false
  },
  {
    "name": "Entra__Graph__ClientID",
    "value": "your-backend-webapi-appid",
    "slotSetting": false
  },
  {
    "name": "Entra__Graph__ClientSecret",
    "value": "your-backend-webapi-secret",
    "slotSetting": false
  },
  {
    "name": "Entra__Instance",
    "value": "https://login.microsoftonline.com/",
    "slotSetting": false
  },
  {
    "name": "Entra__KeyVault__AuthMode",
    "value": "ManagedIdentity",
    "slotSetting": false
  },
  {
    "name": "Entra__TenantID",
    "value": "your-tenantid",
    "slotSetting": false
  },
  {
    "name": "KeyVault__VaultUri",
    "value": "https://your-keyvault-name.vault.azure.net/",
    "slotSetting": false
  }
]
```

## KeyVault configuration

The sample uses Azure KeyVault to store the client secret that is used to access Graph ***if*** you configure `Entra__Graph__AuthMode` to something else than `ManagedIdentity` (like ClientCreds).

Accessing KeyVault can be done via 1) Managed Identity or 2) Client Credentials of the Backend WebApi. You use client credentials for debugging the sample locally and managed identity when running in Azur App Services.

Do the following configuration:

1. Create or use an existing KeyVault.
1. Update the KeyVault Uri in appsettings.json and the `KeyVault__VaultUri` environment variable in Azure App Services.
1. Create a `Secret` with the name of the AppId of the Backend WebApi and with the client secret as the value.
1. In `Access policies`, click `+ Create` to create a `Get - Secret` policy for both the Managed Identity service principal and the Backend WebApi

To enable testing using different authentication method, there is a separate `AuthMode` config setting for KeyVault and for Graph.

## Test run

### In Visual Studio

1. Update appsettings.json
1. Configure KeyVault
1. Start the WebApi
1. Update test.script.ps1 variables with your details
1. Run the test.script.ps1 powershell script

### Powershell test.script.ps1