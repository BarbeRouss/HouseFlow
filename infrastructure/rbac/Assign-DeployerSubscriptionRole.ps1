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
#
# Ce fichier porte un BOM UTF-8 à dessein : sans lui, Windows PowerShell 5.1 le lit en
# ANSI et rend « Rôle » en « RÃ´le ». Ne pas le retirer en croyant nettoyer.
param(
    [Parameter(Mandatory = $true)] [string] $SubscriptionId,
    [Parameter(Mandatory = $true)] [string[]] $AssignableScopeSubscriptionIds,
    [string[]] $SpDisplayName = @(
        "houseflow-github-preview",
        "houseflow-github-prod"
    )
)
$ErrorActionPreference = "Stop"

# `az` est un exécutable externe : un code de retour non nul n'interrompt pas
# PowerShell, et $ErrorActionPreference ne l'attrape pas. Sans cette garde, le
# script annonce « créé » et « assigné » après des échecs — constaté au bootstrap,
# où deux messages de succès ont suivi deux erreurs Azure.
function Invoke-Az {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)
    & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "az $($Arguments -join ' ') a echoue (code $LASTEXITCODE)."
    }
}

$RoleName = "HouseFlow Deployer (subscription)"
$RoleFile = Join-Path $PSScriptRoot "houseflow-deployer-subscription.role.json"

if ($AssignableScopeSubscriptionIds -notcontains $SubscriptionId) {
    throw "La souscription d'assignation ($SubscriptionId) doit figurer dans -AssignableScopeSubscriptionIds, sinon le role n'y sera pas assignable."
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
try {
    $guid = Invoke-Az role definition list --name $RoleName --custom-role-only true --query "[0].name" -o tsv
    if ($guid) {
        # `update` identifie le role par son GUID dans le champ `name`, pas par son nom
        # d'affichage : sans cette injection, Azure ne trouve rien a mettre a jour.
        $withGuid = $roleDefinition | ConvertFrom-Json
        $withGuid.name = $guid
        Set-Content -Path $tmp -Value ($withGuid | ConvertTo-Json -Depth 10) -NoNewline
        Invoke-Az role definition update --role-definition "@$tmp" -o none
        Write-Host "Role '$RoleName' mis a jour ($($scopes.Count) souscriptions assignables)."
    } else {
        Invoke-Az role definition create --role-definition "@$tmp" -o none
        Write-Host "Role '$RoleName' cree ($($scopes.Count) souscriptions assignables)."
    }
} finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}

# 2. Les assignations aux service principals des apps OIDC GitHub
foreach ($sp in $SpDisplayName) {
    $spObjectId = Invoke-Az ad sp list --display-name $sp --query "[0].id" -o tsv
    if (-not $spObjectId) { throw "Service principal '$sp' introuvable." }

    Invoke-Az role assignment create `
        --role $RoleName `
        --scope "/subscriptions/$SubscriptionId" `
        --assignee-object-id $spObjectId `
        --assignee-principal-type ServicePrincipal `
        -o none
    Write-Host "Assignation faite : $sp ($spObjectId) -> '$RoleName' sur /subscriptions/$SubscriptionId."
}
