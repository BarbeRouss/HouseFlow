# Image de base avec .NET 10 et Node.js
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview@sha256:1d5e6f2c1ece7d5826bafc8a7f2d54db2c6478a0f2bd1c995d05a37c0be4783e

# Installer Node.js 20.x
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs

# Installer les dépendances système nécessaires
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
    && rm -rf /var/lib/apt/lists/*

# Installer les workloads .NET Aspire
RUN dotnet workload update \
    && dotnet workload install aspire

# Créer un utilisateur non-root
ARG USERNAME=devuser
ARG USER_UID=1001
ARG USER_GID=$USER_UID

# USER_UID/USER_GID are set to match the host user (see docker-compose.yml build
# args + scripts/feature-env.sh) so the bind-mounted /workspace stays writable.
# The base image may already ship a user/group at that id (e.g. a pre-existing
# "ubuntu" account at 1000:1000) — reuse/rename it instead of failing on conflict.
RUN if getent group "$USER_GID" >/dev/null; then \
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
    && chmod 0440 /etc/sudoers.d/$USERNAME

# Passer à l'utilisateur non-root
USER ${USERNAME}

# Configurer le PATH
ENV PATH="/home/${USERNAME}/.dotnet/tools:${PATH}"
