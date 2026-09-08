# Horizon authentication backend

Node.js/TypeScript API for Horizon Windows. It owns Horizon accounts, linked provider identities, password credentials, verification/reset tokens, sessions, and provider OAuth tokens. The WPF executable receives only Horizon access/refresh credentials and stores them with Windows DPAPI.

## Local development

Requirements: Node.js 22+, pnpm, and PostgreSQL 16+ (or Docker).

```powershell
Copy-Item .env.example .env
docker compose up -d postgres
pnpm install
pnpm migrate
pnpm dev
```

Set `HORIZON_API_BASE_URL=http://127.0.0.1:8787` before launching the WPF app if the default is overridden. Development defaults use console email delivery and do not require email verification; reset and verification codes are returned only when `NODE_ENV` is not `production` and `EMAIL_DELIVERY_MODE=console`.

## Production

Use the included `Dockerfile` behind an HTTPS reverse proxy on Oracle Cloud. Provide `.env` through the deployment platform, run `pnpm migrate` once for each release, and keep PostgreSQL private. Development binds to `127.0.0.1` by default to avoid exposing the API on the LAN; production defaults to `0.0.0.0`, or can be overridden with `HOST`. Production refuses console email delivery. Set `TRUST_PROXY=true` only when the API is behind a trusted reverse proxy.

Provider callback URLs registered in the developer consoles must exactly equal:

- `https://YOUR_API_HOST/v1/auth/oauth/google/callback`
- `https://YOUR_API_HOST/v1/auth/oauth/discord/callback`
- `https://YOUR_API_HOST/v1/auth/oauth/epic/callback`

The desktop redirect is not registered with providers. Horizon opens a random `http://127.0.0.1:<port>/callback` listener, while the backend exchanges provider codes and redirects a short-lived, single-use Horizon code to that listener.

## Verification

```powershell
pnpm typecheck
pnpm test
$env:HORIZON_TEST_BASE_URL = "http://127.0.0.1:8787"
pnpm test:integration
```
