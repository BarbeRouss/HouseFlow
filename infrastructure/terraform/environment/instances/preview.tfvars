# L'hôte des previews de PR.
#
# Seule instance à ne déployer aucune application : elle fournit le réseau, le
# serveur PostgreSQL et le Container Apps Environment, et les PR s'y installent
# en locataires (racine `pr`), une base et deux applications chacune. C'est ce
# qui tient le plafond de trois serveurs PostgreSQL — prod, preprod, preview —
# quel que soit le nombre de PR ouvertes.

name = "preview"

# `expires_at` n'est pas ici : `pr-preview.yml` le recalcule à chaque événement
# de PR, si bien que l'échéance glisse tant qu'une PR est vivante. Une échéance
# figée à la création tuerait cet environnement sous des previews encore
# ouvertes ; il s'éteint donc 12 h après la dernière activité, pas 12 h après
# sa naissance.
rg_lock_enabled = false

bastion_enabled = false
deploy_apps     = false

# `init` crée et supprime la base d'une PR en SQL. `roles` a disparu : plus
# aucun serveur n'est partagé, donc plus aucune identité n'a besoin d'en
# administrer un autre que le sien.
dbtools_jobs = ["init"]

frontend_host = ""
api_host      = ""
