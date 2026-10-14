Import-Module MSAL.PS
Import-Module Microsoft.Graph.Authentication

function LoginClientCredentials( $tenantId, $clientId, $clientSecret, $scope ) {
    $auth = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" `
        -ContentType "application/x-www-form-urlencoded" `
        -Body @{ grant_type="client_credentials"; client_id=$ClientId; client_secret=$ClientSecret; scope=$Scope }
    return @{ 'Content-Type'='application/json'; 'Authorization'='Bearer ' + $auth.access_token }
}

function LoginUser( $tenantId, $clientId, $scope ) {
    # requires redirectUri https://login.microsoftonline.com/common/oauth2/nativeclient as public client + Allow public client flows
    $msalParams = @{ ClientId = $clientId; TenantId = $tenantId; Scopes = "$Scope offline_access" }
    $auth = Get-MsalToken @msalParams -ForceRefresh
    return @{ 'Content-Type'='application/json'; 'Authorization'='Bearer ' + $auth.AccessToken }
}

$TenantId = "your-domain.com or tenantId"
$clientID = "appid of fronend webapp"                       # AppId of frontend webapp
$secret = "secret for frontwebapp"                          # secret of frontend webapp (for app token test)
$scopeBase = "api://appid-of-backend"                       # backend identifierUri
$scopeUser = "$scopeBase/access_as_user"                    # scope for user token
$scopeApp = "$scopeBase/.default"                           # scope for app token

$apiHost="localhost:5001"
$apiHost="webapiusingmanagedidentity-ged4h0c7dqcua8by.westeurope-01.azurewebsites.net"

$authHeader = LoginClientCredentials -TenantId $tenantId -ClientId $clientId -ClientSecret $secret -Scope $scopeApp

Invoke-RestMethod -Method "GET" -Uri "https://$apiHost/api/identity" -Headers $authHeader | ConvertTo-json

Invoke-RestMethod -Method "GET" -Uri "https://$apiHost/api/organization" -Headers $authHeader | ConvertTo-json

$authHeader = LoginUser -TenantId $tenantId -ClientId $clientId -Scope $scopeuser

Invoke-RestMethod -Method "GET" -Uri "https://$apiHost/api/organization" -Headers $authHeader | ConvertTo-json
