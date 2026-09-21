# Crée (ou met à jour) le rôle « HouseFlow Deployer (subscription) » et l'assigne à des
# service principals GitHub OIDC à l'échelle d'une souscription. Nécessaire à tout
# environnement qui déploie des Static Web Apps, ce qui est le cas de tous depuis que
# le frontend Blazor y est servi partout, production comprise. Le rôle est en lecture
# seule.
#
# Une définition de rôle custom est unique par ANNUAIRE, pas par souscription : son nom
# ne peut exister qu'une fois dans le tenant, et c'est `assignableScopes` qui décide où
# elle peut être assignée. D'où les deux paramètres : `-AssignableScopeSubscriptionIds`
# porte la liste complète des souscriptions concernées et doit rester la même à chaque
# appel — la passer partielle retirerait silencieusement les autres — tandis que
# `-SubscriptionId` désigne celle où l'assignation est posée cette fois-ci.
#
# Usage :
#   pwsh infrastructure/rbac/Assign-DeployerSubscriptionRole.ps1 `
#     -SubscriptionId <id-où-assigner> `
#     -AssignableScopeSubscriptionIds <id-prod>,<id-jetable> `
#     -SpDisplayName houseflow-github-prod
#
# Idempotent : relançable sans effet si le rôle et les assignations existent déjà.
param(
    [Parameter(Mandatory = $true)] [string] $SubscriptionId,
    [Parameter(Mandatory = $true)] [string[]] $AssignableScopeSubscriptionIds,
    [string[]] $SpDisplayName = @(
        "houseflow-github-preview",
        "houseflow-github-prod"
    )
)
$ErrorActionPreference = "Stop"

$RoleName = "HouseFlow Deployer (subscription)"
$RoleFile = Join-Path $PSScriptRoot "houseflow-deployer-subscription.role.json"

if ($AssignableScopeSubscriptionIds -notcontains $SubscriptionId) {
    throw "La souscription d'assignation ($SubscriptionId) doit figurer dans -AssignableScopeSubscriptionIds, sinon le rôle n'y sera pas assignable."
}

# 1. Le rôle : une seule action de lecture, assignable dans toutes les souscriptions
#    listées. Le JSON porte un placeholder par souscription pour que sa lecture suffise
#    à comprendre que le rôle en couvre deux.
$scopes = $AssignableScopeSubscriptionIds
$roleDefinition = (Get-Content $RoleFile -Raw) `
    -replace "<SUBSCRIPTION_ID_PROD>", $scopes[0] `
    -replace "<SUBSCRIPTION_ID_EPHEMERAL>", $scopes[1]

$tmp = New-TemporaryFile
Set-Content -Path $tmp -Value $roleDefinition -NoNewline

# Recherche sans `--scope` : le rôle est unique dans l'annuaire, et le chercher dans la
# souscription courante le manquerait tant qu'elle n'est pas encore dans ses scopes.
$existing = az role definition list --name $RoleName --custom-role-only true --query "[0].roleName" -o tsv
if ($existing) {
    az role definition update --role-definition "@$tmp" -o none
    Write-Host "Rôle « $RoleName » mis à jour ($($scopes.Count) souscriptions assignables)."
} else {
    az role definition create --role-definition "@$tmp" -o none
    Write-Host "Rôle « $RoleName » créé ($($scopes.Count) souscriptions assignables)."
}
Remove-Item $tmp

# 2. Les assignations aux service principals des apps OIDC GitHub
foreach ($sp in $SpDisplayName) {
    $spObjectId = az ad sp list --display-name $sp --query "[0].id" -o tsv
    if (-not $spObjectId) { throw "Service principal « $sp » introuvable." }

    az role assignment create `
        --role $RoleName `
        --scope "/subscriptions/$SubscriptionId" `
        --assignee-object-id $spObjectId `
        --assignee-principal-type ServicePrincipal `
        -o none
    Write-Host "Assignation faite : $sp ($spObjectId) → « $RoleName » sur /subscriptions/$SubscriptionId."
}
