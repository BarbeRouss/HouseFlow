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

RUN groupadd --gid $USER_GID $USERNAME \
    && useradd --uid $USER_UID --gid $USER_GID -m -s /bin/bash $USERNAME \
    && echo $USERNAME ALL=\(root\) NOPASSWD:ALL > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME

# Installer Claude CLI globalement (version spécifique)
RUN npm install -g @anthropic-ai/claude-code@2.0.72

# Créer les répertoires de config Claude avec les bonnes permissions
RUN mkdir -p /home/${USERNAME}/.claude /home/${USERNAME}/.config/claude-code \
    && chown -R ${USERNAME}:${USERNAME} /home/${USERNAME}/.claude /home/${USERNAME}/.config/claude-code

# Passer à l'utilisateur non-root
USER ${USERNAME}

# Configurer le PATH
ENV PATH="/home/${USERNAME}/.dotnet/tools:${PATH}"
