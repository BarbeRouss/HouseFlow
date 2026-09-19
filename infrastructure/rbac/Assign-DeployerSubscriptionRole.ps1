# Crée (ou met à jour) le rôle « HouseFlow Deployer (subscription) » et l'assigne
# à un service principal GitHub OIDC à l'échelle de la souscription. Seul l'environnement
# preview en a besoin (état des opérations longues des Static Web Apps).
#
# Usage :
#   pwsh infrastructure/rbac/Assign-DeployerSubscriptionRole.ps1 -SubscriptionId <id> [-SpDisplayName houseflow-github-preview]
# Idempotent : relançable sans effet si le rôle et l'assignation existent déjà.
param(
    [Parameter(Mandatory = $true)] [string] $SubscriptionId,
    [string] $SpDisplayName = "houseflow-github-preview"
)
$ErrorActionPreference = "Stop"

$RoleName = "HouseFlow Deployer (subscription)"
$RoleFile = Join-Path $PSScriptRoot "houseflow-deployer-subscription.role.json"

# 1. Le rôle : une seule action de lecture, assignable à la souscription
$roleDefinition = (Get-Content $RoleFile -Raw) -replace "<SUBSCRIPTION_ID>", $SubscriptionId
$tmp = New-TemporaryFile
Set-Content -Path $tmp -Value $roleDefinition -NoNewline
$existing = az role definition list --name $RoleName --scope "/subscriptions/$SubscriptionId" --query "[0].roleName" -o tsv
if ($existing) {
    az role definition update --role-definition "@$tmp" -o none
    Write-Host "Rôle « $RoleName » mis à jour."
} else {
    az role definition create --role-definition "@$tmp" -o none
    Write-Host "Rôle « $RoleName » créé."
}
Remove-Item $tmp

# 2. L'assignation au service principal de l'app OIDC GitHub
$spObjectId = az ad sp list --display-name $SpDisplayName --query "[0].id" -o tsv
if (-not $spObjectId) { throw "Service principal « $SpDisplayName » introuvable." }

az role assignment create `
    --role $RoleName `
    --scope "/subscriptions/$SubscriptionId" `
    --assignee-object-id $spObjectId `
    --assignee-principal-type ServicePrincipal `
    -o none
Write-Host "Assignation faite : $SpDisplayName ($spObjectId) → « $RoleName » sur /subscriptions/$SubscriptionId."
