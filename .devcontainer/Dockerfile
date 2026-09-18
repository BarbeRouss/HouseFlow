# Image de base avec .NET 10 et Node.js
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:60a2b2230a0d052bc54c0d453e97e331219ed503c5411470ee226859420e693c

# CA optionnelle : sur Claude Code web (ou derrière un proxy d'entreprise) le TLS
# sortant est re-terminé par un proxy dont le conteneur ne connaît pas la CA — sans
# elle, les `curl`/`apt`/`dotnet` du build échouent la vérification TLS.
# scripts/feature-env.sh dépose ici la CA du proxy (ou un fichier VIDE quand il n'y
# en a pas), donc cette étape est un no-op pour un build local classique. Doit rester
# AVANT le premier téléchargement HTTPS ci-dessous.
COPY .devcontainer/proxy-ca.crt /usr/local/share/ca-certificates/proxy-ca.crt
RUN if [ -s /usr/local/share/ca-certificates/proxy-ca.crt ]; then \
      update-ca-certificates; \
    else \
      rm -f /usr/local/share/ca-certificates/proxy-ca.crt; \
    fi
# Node/npm/npx utilisent leur propre magasin de CA : on les pointe sur le magasin
# système (qui inclut désormais la CA du proxy) pour le build ET l'exécution.
ENV NODE_EXTRA_CA_CERTS=/etc/ssl/certs/ca-certificates.crt

# Installer Node.js 20.x
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs

# Installer les dépendances système nécessaires
# build-essential/pkg-config/libssl-dev : requis pour compiler certaines dépendances
# natives du crate Rust (ex. ring/openssl-sys utilisées en transitif par jsonwebtoken,
# rustls) — sans "cc" dans le PATH, cargo build échoue sur ces crates.
RUN apt-get update && apt-get install -y \
    git \
    curl \
    ca-certificates \
    wget \
    postgresql-client \
    sudo \
    gnupg \
    lsb-release \
    apt-transport-https \
    build-essential \
    pkg-config \
    libssl-dev \
    && rm -rf /var/lib/apt/lists/*

# Installer les dépendances système de Playwright/Chromium (E2E frontend Blazor).
# Playwright détecte la distribution et installe les bons paquets apt ; les
# navigateurs eux-mêmes sont installés à la demande par scripts/verify-e2e.sh.
RUN npx --yes playwright@1.49.1 install-deps chromium || true

# Installer les workloads .NET Aspire
RUN dotnet workload update \
    && dotnet workload install aspire

# Installer le toolchain Rust stable (backend rust/houseflow-api, voir rust/PORTING.md).
# Installation système sous /usr/local/{cargo,rustup} (pas dans un $HOME utilisateur) :
# ça marche identiquement que le conteneur tourne en devuser ou en root (cas hôte
# root, voir plus bas) sans dépendre de l'ordre USER/COPY. rustup-init doit respecter
# la CA du proxy déjà installée plus haut (update-ca-certificates avant ce téléchargement).
ENV RUSTUP_HOME=/usr/local/rustup \
    CARGO_HOME=/usr/local/cargo \
    PATH=/usr/local/cargo/bin:$PATH
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | \
      sh -s -- -y --default-toolchain stable --profile minimal --no-modify-path \
    && rustup component add clippy rustfmt \
    && chmod -R a+w "$RUSTUP_HOME" "$CARGO_HOME"

# Créer un utilisateur non-root
ARG USERNAME=devuser
ARG USER_UID=1001
ARG USER_GID=$USER_UID

# USER_UID/USER_GID are set to match the host user (see docker-compose.yml build
# args + scripts/feature-env.sh) so the bind-mounted /workspace stays writable.
# The base image may already ship a user/group at that id (e.g. a pre-existing
# "ubuntu" account at 1000:1000) — reuse/rename it instead of failing on conflict.
#
# Cas hôte root (uid 0, ex. Claude Code web) : le conteneur doit tourner en root
# pour écrire dans le bind mount /workspace possédé par root — on saute la création
# d'utilisateur (renommer le compte root échoue) et feature-env.sh passe USERNAME=root.
RUN if [ "$USER_UID" = "0" ]; then \
      echo "Host user is root (uid 0) — container runs as root, no user alignment needed"; \
    else \
      if getent group "$USER_GID" >/dev/null; then \
        groupmod -n $USERNAME "$(getent group "$USER_GID" | cut -d: -f1)"; \
      else \
        groupadd --gid $USER_GID $USERNAME; \
      fi \
      && if getent passwd "$USER_UID" >/dev/null; then \
        usermod -l $USERNAME -d /home/$USERNAME -m -g $USER_GID "$(getent passwd "$USER_UID" | cut -d: -f1)"; \
      else \
        useradd --uid $USER_UID --gid $USER_GID -m -s /bin/bash $USERNAME; \
      fi \
      && echo $USERNAME ALL=\(root\) NOPASSWD:ALL > /etc/sudoers.d/$USERNAME \
      && chmod 0440 /etc/sudoers.d/$USERNAME; \
    fi

# Passer à l'utilisateur applicatif (root quand l'hôte est root, sinon devuser)
USER ${USERNAME}

# Configurer le PATH (outils dotnet globaux). On ajoute les deux emplacements
# possibles : /root/.dotnet/tools quand l'hôte est root, /home/<user>/.dotnet/tools
# sinon — un chemin inexistant dans PATH est un no-op sans effet.
ENV PATH="/root/.dotnet/tools:/home/${USERNAME}/.dotnet/tools:${PATH}"
