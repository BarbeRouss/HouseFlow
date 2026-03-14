# Déploiement HouseFlow

## Prérequis

- VM Ubuntu 22.04+ ou Debian 12+ sur Proxmox
- 2 CPU, 4GB RAM, 40GB disk minimum
- Accès root ou sudo
- Domaine configuré (DNS A records vers l'IP de la VM)

## Setup rapide

```bash
# Sur la VM, en root :
curl -sSL https://raw.githubusercontent.com/BarbeRouss/HouseFlow/main/infrastructure/setup-vm.sh | sudo bash
```

Ou manuellement :

```bash
git clone https://github.com/BarbeRouss/HouseFlow.git /tmp/houseflow-setup
sudo bash /tmp/houseflow-setup/infrastructure/setup-vm.sh
```

## Post-installation

### 1. Configurer les secrets

```bash
# Éditer les .env pour prod et preprod
sudo -u houseflow nano /opt/houseflow/prod/.env
sudo -u houseflow nano /opt/houseflow/preprod/.env
```

### 2. Login GHCR

```bash
sudo -u houseflow docker login ghcr.io -u barberouss
# Entrer un Personal Access Token avec scope read:packages
```

### 3. Premier démarrage

```bash
# Prod
cd /opt/houseflow/prod
sudo -u houseflow docker compose up -d

# Preprod
cd /opt/houseflow/preprod
sudo -u houseflow docker compose up -d
```

### 4. Configurer Traefik

Traefik est géré séparément. Configurer les routes :

| Domaine | Destination |
|---------|-------------|
| `app.yourdomain.com` | `localhost:3000` |
| `api.yourdomain.com` | `localhost:8080` |
| `preprod.yourdomain.com` | `localhost:3100` |
| `api-preprod.yourdomain.com` | `localhost:8180` |

### 5. Configurer GitHub

**Secrets du repo :**

| Secret | Valeur |
|--------|--------|
| `DEPLOY_HOST` | IP publique ou DDNS de la VM |
| `DEPLOY_USER` | `houseflow` |
| `DEPLOY_SSH_KEY` | Clé privée SSH de l'utilisateur houseflow |

**Environments :**

| Environment | Protection |
|-------------|-----------|
| `preprod` | Aucune (auto-deploy) |
| `production` | Required reviewers |

### 6. Générer la clé SSH pour le CI

```bash
# Sur la VM, en tant que houseflow
sudo -u houseflow ssh-keygen -t ed25519 -C "github-deploy" -f /home/houseflow/.ssh/github_deploy -N ""

# Ajouter la clé publique aux authorized_keys
sudo -u houseflow bash -c 'cat /home/houseflow/.ssh/github_deploy.pub >> /home/houseflow/.ssh/authorized_keys'

# Copier la clé privée → secret GitHub DEPLOY_SSH_KEY
sudo cat /home/houseflow/.ssh/github_deploy
```

## Architecture sur la VM

```
/opt/houseflow/
├── prod/
│   ├── docker-compose.yaml
│   └── .env
├── preprod/
│   ├── docker-compose.yaml
│   └── .env
├── scripts/
│   ├── backup.sh          (cron quotidien 3h)
│   └── sync-db-to-preprod.sh
└── backups/
    └── houseflow_YYYYMMDD_HHMMSS.dump.gz
```

## Pipeline CI/CD

```
Push main → Build images (CalVer) → GHCR → Preprod (auto) → Approval → Prod
Workflow dispatch → Build → Preprod (any branch, no prod)
```

## Versioning (CalVer)

Format : `YYYY.MM.DD` avec suffixe si nécessaire.

```
2026.03.14        ← première release du jour
2026.03.14-2      ← deuxième release du même jour
2026.03.14-feat-x ← depuis une feature branch
```

## Commandes utiles

```bash
# Voir les logs
cd /opt/houseflow/prod && docker compose logs -f

# Redémarrer un service
cd /opt/houseflow/prod && docker compose restart api

# Backup manuel
/opt/houseflow/scripts/backup.sh

# Sync DB prod → preprod
/opt/houseflow/scripts/sync-db-to-preprod.sh

# Voir les backups
ls -la /opt/houseflow/backups/
```
