# ─── Runtime environment variables (Docs/Requirements.md §6) ────────────────
# Build-time (baked into the JS bundle by `next build` -- see the `builder`
# stage's ARGs below, NOT read from `.env` which is excluded via
# .dockerignore):
#   NEXT_PUBLIC_GATEWAY_URL          browser-facing Gateway base URL (SignalR)
#   NEXT_PUBLIC_IMAGE_STORAGE_URL    object-storage host (next/image remotePatterns)
#
# Runtime-only (read from `process.env` by the standalone `server.js` at
# container start -- supply via `docker run -e` / compose `environment:` /
# a Kubernetes Secret; NEVER baked into the image, NEVER read from a committed
# `.env`):
#   AUTH_SECRET                      next-auth session/JWT signing secret
#   AUTH_URL                         public base URL of this app
#   AUTH_TRUST_HOST                  required for self-hosted (non-Vercel) auth.ts
#   AUTH_IDENTITYSERVER_ISSUER       IdentityServer issuer URL
#   AUTH_IDENTITYSERVER_ID           OIDC client id ("webapp")
#   AUTH_IDENTITYSERVER_SECRET       OIDC client secret
#   AUTH_IDENTITYSERVER_ALLOW_INSECURE_TLS   dev-only, see lib/dev-tls.ts
#   GATEWAY_URL                      server-only Gateway base URL (API calls)
# See .env.example for the full description of each variable.

# ─── deps stage ─────────────────────────────────────────────────────────────
# Installs the full npm dependency tree (incl. devDependencies -- `next build`
# needs typescript/tailwindcss) in its own cached layer, keyed only on the
# lockfiles so it's skipped on source-only edits.
FROM node:22-alpine AS deps
WORKDIR /app

# Required on Alpine (musl libc) for some native/prebuilt npm packages to
# resolve correctly -- standard Next.js-on-Alpine guidance.
RUN apk add --no-cache libc6-compat

# package-lock.json before the rest of the source so `npm ci` lands in its own
# cached layer and only re-runs when a dependency changes, not on every edit.
COPY package.json package-lock.json ./
RUN npm ci

# ─── builder stage ──────────────────────────────────────────────────────────
FROM node:22-alpine AS builder
WORKDIR /app

COPY --from=deps /app/node_modules ./node_modules
COPY . .

# NEXT_PUBLIC_* values are inlined into the client JS bundle at build time --
# there is no way to change them at container-run time (see next.config.ts /
# lib/image-config.ts). Defaults here match .env.example's local-dev values so
# a plain `docker build` with no --build-arg still produces a working local
# image; a real deployment overrides both with the environment's actual
# Gateway/object-storage hostnames, e.g.:
#   docker build \
#     --build-arg NEXT_PUBLIC_GATEWAY_URL=https://api.example.com \
#     --build-arg NEXT_PUBLIC_IMAGE_STORAGE_URL=https://images.example.com .
ARG NEXT_PUBLIC_GATEWAY_URL=http://localhost:6001
ARG NEXT_PUBLIC_IMAGE_STORAGE_URL=http://localhost:9000
ENV NEXT_PUBLIC_GATEWAY_URL=${NEXT_PUBLIC_GATEWAY_URL}
ENV NEXT_PUBLIC_IMAGE_STORAGE_URL=${NEXT_PUBLIC_IMAGE_STORAGE_URL}

ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

# ─── runner stage ───────────────────────────────────────────────────────────
FROM node:22-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1

# Dedicated non-root user/group (explicit uid/gid, not a base-image default).
RUN addgroup --system --gid 1001 nodejs \
    && adduser --system --uid 1001 --ingroup nodejs nextjs

# Static assets -- the standalone server does not serve `public/` or
# `.next/static` on its own (see next.config.ts's `output: "standalone"`
# comment); both must be copied in alongside the traced server bundle.
COPY --from=builder /app/public ./public
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static

USER nextjs

EXPOSE 3000
ENV PORT=3000
ENV HOSTNAME=0.0.0.0

# No HEALTHCHECK here, matching the backend Dockerfiles' convention -- health
# is verified via Docker Compose `healthcheck:` blocks (Phase 8), not baked
# into the image. This app's dependency-free liveness probe now exists at
# `GET /api/health` (Task 20, app/api/health/route.ts) -- a compose/k8s
# healthcheck block can point at it once Phase 8 adds one.
CMD ["node", "server.js"]
