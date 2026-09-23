#!/usr/bin/env bash
# Écrit la configuration d'exécution du frontend Blazor dans un wwwroot publié.
#
#   write-runtime-config.sh <wwwroot> <api-base-url> <demo-mode>
#
# Blazor WebAssembly n'a pas de processus serveur : il lit ce fichier en HTTP au
# démarrage (src/HouseFlow.Web/Program.cs, LoadRuntimeConfigAsync). C'est donc un
# fichier statique comme un autre, et rien n'oblige à le figer au build — le figer
# est même ce qui interdit de partager un artefact entre environnements, puisque
# l'URL de l'API n'est connue qu'une fois l'environnement appliqué.
#
# D'où ce script, appelé après `terraform apply` et avant le téléversement, par la
# production comme par les environnements de PR : un seul wwwroot compilé, accordé
# à son environnement au dernier moment.
#
# Les clés sont en PascalCase à dessein : le frontend les lit avec JsonDocument,
# qui est sensible à la casse. Les renommer casserait la lecture en silence.
set -euo pipefail

wwwroot=${1:?"usage: write-runtime-config.sh <wwwroot> <api-base-url> <demo-mode>"}
api_base_url=${2:?"URL de l'API manquante"}
demo_mode=${3:?"mode démo manquant"}

[ -d "$wwwroot" ] || { echo "::error::wwwroot introuvable : $wwwroot" >&2; exit 1; }

# Les gardes `:?` ci-dessus couvrent l'absent et le vide. Restent les valeurs non
# vides mais aberrantes — `terraform output -raw` rend la chaîne « null » quand la
# sortie vaut null, et une URL en http:// serait refusée par le navigateur en page
# https. Sans ce contrôle, le frontend repartirait en silence sur une API injoignable,
# ce qui est exactement la panne que ce script existe pour empêcher.
case "$api_base_url" in
  https://*) ;;
  *) echo "::error::URL d'API inattendue : '$api_base_url' (https:// attendu)" >&2; exit 1 ;;
esac

case "$demo_mode" in
  true|false) ;;
  *) echo "::error::mode démo inattendu : '$demo_mode' (true ou false attendu)" >&2; exit 1 ;;
esac

printf '{ "ApiBaseUrl": "%s", "DemoMode": "%s" }\n' "$api_base_url" "$demo_mode" \
  > "$wwwroot/appsettings.json"

echo "Configuration d'exécution écrite dans $wwwroot/appsettings.json :"
cat "$wwwroot/appsettings.json"
