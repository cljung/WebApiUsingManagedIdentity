Connect-MgGraph -Scopes "Directory.Read.All"

$ManagedIdentityObjectId = "YOUR_FUNCTION_MANAGED_IDENTITY_OBJECT_ID"
$GraphServicePrincipal = Get-MgServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'"

$AppRole = $GraphServicePrincipal.AppRoles | Where-Object {$_.Value -eq "Directory.Read.All" -and $_.AllowedMemberTypes -contains "Application"}

New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $ManagedIdentityObjectId `
										-PrincipalId $ManagedIdentityObjectId `
										-ResourceId $GraphServicePrincipal.Id `
										-AppRoleId $AppRole.Id
