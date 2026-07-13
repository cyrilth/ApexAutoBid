#!/bin/sh
# ApexAutoBid — dev TLS material for the docker-compose stack (Phase 8 Task 4).
#
# Runs once as the `devcerts` init container (docker/docker-compose.yml) and writes
# into the shared `nginx-certs` volume:
#
#   /certs/apexautobid.local.crt|.key   wildcard cert for *.apexautobid.local
#   /certs/ca/ca.crt                    the dev CA that signed it
#
# nginx-proxy resolves a vhost's cert by filename: app.apexautobid.local first looks
# for app.apexautobid.local.crt, then falls back to the parent-domain-named wildcard
# file apexautobid.local.crt — one wildcard cert therefore covers all four dev
# domains (app./api./id./storage.apexautobid.local).
#
# The CA CERTIFICATE alone (never any private key) is additionally copied to the
# separate `ca-trust` volume, mounted at /ca-trust here and read-only into the app
# containers — deliberately a different volume from the one nginx reads, so that a
# compromised app container can see only the public cert, not the CA/server keys
# it would need to mint certificates other machines trust. App containers use it
# for THEIR OWN server-side calls to the https dev domains (which resolve to the
# Nginx container via network aliases):
#   - Node (web-app): NODE_EXTRA_CA_CERTS=/certs/ca/ca.crt — additive, public roots
#     remain trusted.
#   - .NET services:  SSL_CERT_FILE=/certs/ca/ca.crt — this REPLACES the OpenSSL
#     default root store, so it is only set on services whose outbound TLS is
#     exclusively the dev domains (Auction/Bidding/Gateway/Notification). It is
#     deliberately NOT set on IdentityService, which must keep the public roots to
#     reach Cloudflare's Turnstile siteverify endpoint.
#
# Browsers show a warning for these domains unless ca.crt is imported into the
# client machine's trust store — acceptable for local dev; docker/README notes how.
#
# Idempotent: keeps existing material so the CA (and any client-side trust of it)
# survives `docker compose up` re-runs. Delete the nginx-certs volume to rotate.
#
# Everything here is throwaway, generated-on-first-run, dev-only material — the CA
# key never leaves the named volume and signs nothing but *.apexautobid.local.
# Production replaces this entire mechanism with acme-companion/Let's Encrypt
# (see docker-compose.yml's `production` profile).

set -eu

CERT_DIR=/certs
CA_DIR="$CERT_DIR/ca"
TRUST_DIR=/ca-trust
DOMAIN=apexautobid.local

# The material is one atomic set: nginx needs the wildcard cert AND its key, and
# future signing needs the CA key. If ANY piece is missing (partial cleanup,
# interrupted first run), fall through and regenerate everything — a stale public
# cert without its key would otherwise leave nginx unable to serve TLS.
if [ -f "$CERT_DIR/$DOMAIN.crt" ] && [ -f "$CERT_DIR/$DOMAIN.key" ] \
    && [ -f "$CA_DIR/ca.crt" ] && [ -f "$CA_DIR/ca.key" ] \
    && [ -f "$TRUST_DIR/ca.crt" ]; then
    echo "devcerts: existing certificate material found, nothing to do"
    exit 0
fi

mkdir -p "$CA_DIR" "$TRUST_DIR"

echo "devcerts: generating dev CA"
openssl genrsa -out "$CA_DIR/ca.key" 2048
openssl req -x509 -new -nodes -key "$CA_DIR/ca.key" -sha256 -days 825 \
    -subj "/CN=ApexAutoBid Dev CA" \
    -out "$CA_DIR/ca.crt"

echo "devcerts: generating wildcard certificate for *.$DOMAIN"
openssl genrsa -out "$CERT_DIR/$DOMAIN.key" 2048
openssl req -new -key "$CERT_DIR/$DOMAIN.key" \
    -subj "/CN=*.$DOMAIN" \
    -out "$CERT_DIR/$DOMAIN.csr"

cat > "$CERT_DIR/san.ext" <<EOF
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:*.$DOMAIN,DNS:$DOMAIN
EOF

openssl x509 -req -in "$CERT_DIR/$DOMAIN.csr" \
    -CA "$CA_DIR/ca.crt" -CAkey "$CA_DIR/ca.key" -CAcreateserial \
    -days 825 -sha256 -extfile "$CERT_DIR/san.ext" \
    -out "$CERT_DIR/$DOMAIN.crt"

rm -f "$CERT_DIR/$DOMAIN.csr" "$CERT_DIR/san.ext"

# Private keys stay root-only readable — this volume is mounted ONLY by nginx
# (whose master process runs as root) and this container; the certificates are
# world-readable public material.
chmod 600 "$CERT_DIR/$DOMAIN.key" "$CA_DIR/ca.key"
chmod 644 "$CERT_DIR/$DOMAIN.crt" "$CA_DIR/ca.crt"

# Public trust copy for the app containers (separate volume — see header).
cp "$CA_DIR/ca.crt" "$TRUST_DIR/ca.crt"
chmod 644 "$TRUST_DIR/ca.crt"

echo "devcerts: done — wildcard cert + CA in nginx-certs; public ca.crt in ca-trust"
