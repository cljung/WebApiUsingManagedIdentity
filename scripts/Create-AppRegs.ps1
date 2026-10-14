# Import-Module Microsoft.Graph.Applications
Connect-MgGraph -Scopes "Application.ReadWrite.All", "AppRoleAssignment.ReadWrite.All"

$currentContext = Get-MgContext
$tenantId = $currentContext.TenantId
$currentUPN = $currentContext.Account
$domain = (Get-MgContext).Account.Split('@')[1]
$currentUserProfile = Invoke-MgGraphRequest -Method GET -Uri "v1.0/me"
$currentUserId = $currentUserProfile.id

# 1. Define the parameters explicitly extracted from your manifest
$AppName = "WebApiUsingManagedIdentity-BackEnd-test"
$AppNameFrontEnd = "WebApiUsingManagedIdentity-FrontEnd-test"

$WebRedirectUris = @("http://localhost")

$ScopeId = [Guid]::NewGuid().ToString()
$AppRoleId = [Guid]::NewGuid().ToString()
# 2. Build the OAuth2 Delegated Permission Scope object (api.oauth2PermissionScopes)
$OAuth2Scope = @{
    Id                      = $ScopeId
    AdminConsentDescription = "access_as_user"
    AdminConsentDisplayName = "access_as_user"
    UserConsentDescription  = "access_as_user"
    UserConsentDisplayName  = "access_as_user"
    Value                   = "access_as_user"
    Type                    = "Admin"
    IsEnabled               = $true
}

# 3. Build the Application App Role object (appRoles)
$AppRole = @{
    Id                 = $AppRoleId
    AllowedMemberTypes = @("Application")
    Description        = "Api.Read"
    DisplayName        = "Api.Read"
    Origin             = "Application"
    IsEnabled          = $true
    Value              = "Api.Read"
}

# 4. Build the Required Resource Access object (Graph API permissions: email and User.Read)
$RequiredResourceAccess = @(
    @{
        ResourceAppId  = "00000003-0000-0000-c000-000000000000" # Microsoft Graph
        ResourceAccess = @(
            @{
                Id   = "4908d5b9-3fb2-4b1e-9336-1888b7937185" # Organization.Read.All
                Type = "Scope"
            },
            @{
                Id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d" # User.Read
                Type = "Scope"
            }
        )
    }
)

# 5. Assemble the final Application Creation Request Payload
$AppParams = @{
    DisplayName            = $AppName
    SignInAudience         = "AzureADMyOrg"
    RequiredResourceAccess = $RequiredResourceAccess
    AppRoles               = @($AppRole)
    Api                    = @{
        Oauth2PermissionScopes = @($OAuth2Scope)
    }
    Web                    = @{
        RedirectUris = $WebRedirectUris
        ImplicitGrantSettings = @{
            EnableAccessTokenIssuance = $false
            EnableIdTokenIssuance      = $false
        }
    }
}


# 6. Execute the registration
Write-Host "Creating App Registration: $AppName..." -ForegroundColor Cyan
$NewApp = New-MgApplication @AppParams

$identifierUri = "api://$($NewApp.AppId)"
Write-Host "Updating Application with Identifier URI..." -ForegroundColor Cyan
Update-MgApplication -ApplicationId $NewApp.Id -IdentifierUris @($identifierUri)

# 7. Add the client secret credential (passwordCredentials)
# Note: Entra ID generates the secret string value upon creation; it cannot be pre-defined.
$PasswordParams = @{
    ApplicationId = $NewApp.Id
    PasswordCredential = @{
        DisplayName  = "appsettings.json"
        StartDateTime = [DateTime]::UtcNow
        EndDateTime   = [DateTime]::UtcNow.AddMonths(6) # Maps to your ~6-month window expiry
    }
}

Write-Host "Adding Client Secret Credential..." -ForegroundColor Cyan
$NewSecret = Add-MgApplicationPassword @PasswordParams

Write-Host "Creating App Service Principal..." -ForegroundColor Cyan
$sp = New-MgServicePrincipal -AppId $NewApp.AppId 

Write-Host "Assigning user $($currentUPN) as owner..." -ForegroundColor Cyan
$ownerPayload = @{
    "@odata.id" = "https://graph.microsoft.com/v1.0/users/$currentUserId"
}
New-MgApplicationOwnerByRef -ApplicationId $Newapp.Id -BodyParameter $ownerPayload -ErrorAction Stop

# 8. Output resulting IDs to console
<#
Write-Host "`nApp Registration Successfully Created!" -ForegroundColor Green
[PSCustomObject]@{
    "Application Name" = $NewApp.DisplayName
    "Object ID"        = $NewApp.Id
    "Client (App) ID"  = $NewApp.AppId
    "Client Secret"    = $NewSecret.SecretText # <-- Save this immediately, it only shows once!
} | Format-List
#>
############################################################################################
# frontend

$BackEndAppId = $Newapp.AppId

$RequiredResourceAccess = @(
    # Permissions targeting your Back-End API
    @{
        ResourceAppId  = $BackEndAppId
        ResourceAccess = @(
            @{
                Id   = $ScopeId # access_as_user (Delegated / Scope)
                Type = "Scope"
            },
            @{
                Id   = $AppRoleId # Api.Read (Application / Role)
                Type = "Role"
            }
        )
    },
    # Permissions targeting Microsoft Graph
    @{
        ResourceAppId  = "00000003-0000-0000-c000-000000000000"
        ResourceAccess = @(
            @{
                Id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d" # User.Read (Delegated / Scope)
                Type = "Scope"
            }
        )
    }
)

$AppParams = @{
    DisplayName            = $AppNameFrontEnd
    SignInAudience         = "AzureADMyOrg"
    IsFallbackPublicClient = $true     # Matches "isFallbackPublicClient": true
    RequiredResourceAccess = $RequiredResourceAccess
    
    # Web Platform Settings (Includes Implicit Flow configuration)
    Web = @{
        RedirectUris = @("http://localhost")
        ImplicitGrantSettings = @{
            EnableAccessTokenIssuance = $true # Matches enableAccessTokenIssuance: true
            EnableIdTokenIssuance      = $true # Matches enableIdTokenIssuance: true
        }
    }
    
    # Public Client / Native Platform Settings
    PublicClient = @{
        RedirectUris = @("https://login.microsoftonline.com/common/oauth2/nativeclient")
    }
}

Write-Host "Creating Front-End App Registration: $AppNameFrontEnd..." -ForegroundColor Cyan
$NewAppFE = New-MgApplication @AppParams

$PasswordParams = @{
    ApplicationId = $NewAppFE.Id
    PasswordCredential = @{
        DisplayName   = "test script"
        StartDateTime = [DateTime]::UtcNow
        EndDateTime   = [DateTime]::UtcNow.AddMonths(6) # Maps to the ~6 month window in the manifest
    }
}

Write-Host "Generating Client Secret..." -ForegroundColor Cyan
$NewSecretFE = Add-MgApplicationPassword @PasswordParams

Write-Host "Creating App Service Principal..." -ForegroundColor Cyan
$spFE = New-MgServicePrincipal -AppId $NewAppFE.AppId 

Write-Host "Assigning user $($currentUPN) as owner..." -ForegroundColor Cyan
$ownerPayload = @{
    "@odata.id" = "https://graph.microsoft.com/v1.0/users/$currentUserId"
}
New-MgApplicationOwnerByRef -ApplicationId $NewappFE.Id -BodyParameter $ownerPayload -ErrorAction Stop

<#
Write-Host "`nApp Registration Successfully Created!" -ForegroundColor Green
[PSCustomObject]@{
    "Application Name" = $NewAppFE.DisplayName
    "Object ID"        = $NewAppFE.Id
    "Client (App) ID"  = $NewAppFE.AppId
    "Client Secret"    = $NewSecretFE.SecretText # <-- Capture this instantly; it cannot be retrieved later!
} | Format-List
#>

$appsettings = (Get-Content "../appsettings.json" | ConvertFrom-json)
$appsettings.'Entra'.TenantId = $tenantId
$appsettings.'Entra'.Domain = $domain
$appsettings.'Entra'.ClientId = $NewAppFE.AppId
$appsettings.'Entra'.ClientSecret = $NewSecretFE.SecretText
$appsettings.'Entra'.Audience = "api://$($Newapp.AppId)"
$appsettings.'Entra'.'Graph'.ClientId = $NewApp.AppId
$appsettings.'Entra'.'Graph'.ClientSecret = $NewSecret.SecretText
$appsettings.'KeyVault'.SecretName = $NewApp.AppId

write-host "`nappsettings.json details:`n"
$appsettings | ConvertTo-json

write-host "*** TODO ***" -ForegroundColor Green
write-host "1) Create a KeyVault secret with the name $($NewApp.AppId) and value $($NewSecret.SecretText)" -ForegroundColor Green
write-host "2) Grant Get Secret access to app $($NewAppFE.AppID) in KeyVault" -ForegroundColor Green
write-host "3) Update appsettings.json with your KeyVaultUri" -ForegroundColor Green
write-host "4) Test client credentials work" -ForegroundColor Green
write-host "5) Switch to managed identity " -ForegroundColor Green