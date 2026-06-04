FROM node:22-alpine@sha256:4d64b49e6c891c8fc821007cb1cdc6c0db7773110ac2c34bf2e6960adef62ed3 AS deps
WORKDIR /app
COPY package*.json ./
RUN npm ci

FROM node:22-alpine@sha256:4d64b49e6c891c8fc821007cb1cdc6c0db7773110ac2c34bf2e6960adef62ed3 AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

FROM node:22-alpine@sha256:4d64b49e6c891c8fc821007cb1cdc6c0db7773110ac2c34bf2e6960adef62ed3 AS runtime
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1

RUN addgroup --system --gid 1001 nodejs && adduser --system --uid 1001 nextjs

# Standalone output includes everything needed to run
COPY --from=build --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=build --chown=nextjs:nodejs /app/.next/static ./.next/static

USER nextjs
EXPOSE 3000
ENV HOSTNAME="0.0.0.0"
CMD ["node", "server.js"]
