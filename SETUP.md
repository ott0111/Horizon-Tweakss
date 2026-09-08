# Horizon setup guide

This guide is written for someone who has not configured a backend before. Follow the sections in order. You do not need to understand OAuth, PostgreSQL, or SMTP to run Horizon locally.

## Your two environment files

- Safe public template: `backend/.env.example`
- Private file that Horizon actually reads: `backend/.env`

On this development PC their full paths are:

```text
C:\Users\Ali Hassan\Documents\Codex\2026-08-13\https-github-com-lachlan1234564-horzion-labs\work\repo\backend\.env.example
C:\Users\Ali Hassan\Documents\Codex\2026-08-13\https-github-com-lachlan1234564-horzion-labs\work\repo\backend\.env
```

Codex has already created `backend/.env`. This setup pass detected that it exists and preserved it; no existing values were overwritten.

Edit **`backend/.env` only** when adding real credentials. Keep `.env.example` as a safe example for GitHub. Never paste real passwords, API keys, or client secrets into `.env.example`, source code, screenshots, chat, issues, or commits.

The repository's `.gitignore` ignores `.env` and `.env.*`, while explicitly allowing `.env.example`. `backend/.env` must never be uploaded to GitHub.

## What the unfamiliar words mean

- **Environment variable:** one named setting read by the backend, such as `PORT=8787`.
- **PostgreSQL:** the database that permanently stores Horizon accounts and sessions.
- **OAuth:** the browser flow behind “Continue with Google/Discord/Epic.” The provider confirms the person's identity and sends the result to Horizon's backend.
- **Client ID:** a public identifier for Horizon in a provider dashboard.
- **Client secret:** a private password used only by the Horizon backend. It must never be placed in the WPF app.
- **Redirect URI:** the exact backend address to which a provider returns the browser after sign-in. The spelling, port, path, and trailing slash must match exactly.
- **SMTP:** the standard service the backend uses to send verification and password-reset email.

## 1. Run local development first

### Install the prerequisites

Install these if they are not already available:

- [Node.js 22 LTS or newer](https://nodejs.org/en/download)
- [Docker Desktop for Windows](https://docs.docker.com/desktop/setup/install/windows-install/) (the easiest local PostgreSQL option)
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

Open PowerShell in the repository folder and enable the package manager:

```powershell
corepack enable
corepack prepare pnpm@11.19.0 --activate
```

### Start PostgreSQL and the backend

The included Docker database uses these development-only values:

```text
database: horizon
user: horizon
password: horizon_dev_only
address: 127.0.0.1:5432
```

For Docker development, make this one line in `backend/.env` exactly:

```dotenv
DATABASE_URL=postgresql://horizon:horizon_dev_only@127.0.0.1:5432/horizon
```

Then run:

```powershell
Set-Location "C:\Users\Ali Hassan\Documents\Codex\2026-08-13\https-github-com-lachlan1234564-horzion-labs\work\repo\backend"
docker compose up -d postgres
pnpm install
pnpm migrate
pnpm dev
```

Keep that PowerShell window open. The last command runs the API. In another PowerShell window, check it:

```powershell
Invoke-RestMethod http://127.0.0.1:8787/health
```

Authentication infrastructure is connected when the response says `status` and `database` are both `ok`.

### Start Horizon against the local backend

The WPF app already defaults to `http://127.0.0.1:8787`. Set it explicitly for this PowerShell session and launch the app:

```powershell
Set-Location "C:\Users\Ali Hassan\Documents\Codex\2026-08-13\https-github-com-lachlan1234564-horzion-labs\work\repo"
$env:HORIZON_API_BASE_URL = "http://127.0.0.1:8787"
dotnet run --project src\Horizon.App\Horizon.App.csproj
```

For the published build, use:

```powershell
./scripts/build-release.ps1
$env:HORIZON_API_BASE_URL = "http://127.0.0.1:8787"
./artifacts/win-x64/Horizon.exe
```

You can tell email/password authentication is working when all of this succeeds:

1. **Create Account** accepts a display name, email, and password.
2. Horizon opens Onboarding.
3. Completing Onboarding opens Dashboard.
4. Sign out, then sign in with the same email and password.
5. `http://127.0.0.1:8787/v1/auth/providers` opens and reports a controlled availability status for Google, Discord, and Epic.

For this local test, keep these development values:

```dotenv
NODE_ENV=development
REQUIRE_EMAIL_VERIFICATION=false
EMAIL_DELIVERY_MODE=console
```

Google, Discord, Epic, and SMTP may all remain blank during local email/password development. Their buttons will say that sign-in is currently unavailable instead of crashing.

## 2. Configure Google sign-in

Horizon uses a **Web application** OAuth client because the backend, not the desktop executable, holds the secret.

1. Open the [Google Cloud Console](https://console.cloud.google.com/) and sign in.
2. Use the project selector at the top and choose **New Project**. Name it `Horizon` and create it.
3. Open [Google Auth Platform → Branding](https://console.cloud.google.com/auth/branding). Click **Get started** if shown. Enter `Horizon`, a user-support email, and your developer contact email. Save.
4. Open [Google Auth Platform → Audience](https://console.cloud.google.com/auth/audience). Choose **External** unless Horizon is restricted to one Google Workspace organization. During development, keep the app in **Testing** and add your own Google address under **Test users** if Google asks for one.
5. Open **Data Access** in Google Auth Platform. Horizon requests only `openid`, `email`, and `profile`. Do not add Drive, Gmail, Calendar, or other unrelated scopes.
6. Open [Google Auth Platform → Clients](https://console.cloud.google.com/auth/clients), click **Create client**, and choose **Web application**.
7. Name it `Horizon backend`. No JavaScript origin is required.
8. Under **Authorized redirect URIs**, add the exact URI used by this environment:

   ```text
   http://127.0.0.1:8787/v1/auth/oauth/google/callback
   ```

   For production, add a second URI using the public HTTPS API host, for example:

   ```text
   https://auth.example.com/v1/auth/oauth/google/callback
   ```

9. Click **Create**. Copy the displayed Client ID and Client secret into `backend/.env`:

   ```dotenv
   GOOGLE_CLIENT_ID=the Client ID from Google
   GOOGLE_CLIENT_SECRET=the Client secret from Google
   GOOGLE_REDIRECT_URI=http://127.0.0.1:8787/v1/auth/oauth/google/callback
   ```

- `GOOGLE_CLIENT_ID` is not secret.
- `GOOGLE_CLIENT_SECRET` **is secret**.
- `GOOGLE_REDIRECT_URI` is not secret, but it must exactly match the entry in Google.

Google's official instructions are in [Create access credentials](https://developers.google.com/workspace/guides/create-credentials) and [OAuth for web-server applications](https://developers.google.com/identity/protocols/oauth2/web-server).

Restart `pnpm dev` after saving `.env`. Open Horizon, press the Google button, finish sign-in in the system browser, and confirm Horizon returns to Onboarding or Dashboard.

## 3. Configure Discord sign-in

1. Open the [Discord Developer Portal](https://discord.com/developers/applications) and sign in.
2. Click **New Application**, name it `Horizon`, accept the terms, and create it.
3. On **General Information**, optionally upload Horizon's icon. Copy **Application ID**; this is the Client ID.
4. Open **OAuth2** in the left sidebar.
5. Copy **Client ID**. Under **Client Secret**, click **Reset Secret** if no secret is visible, complete Discord's confirmation, and copy the new secret immediately.
6. In **Redirects**, click **Add Redirect** and enter:

   ```text
   http://127.0.0.1:8787/v1/auth/oauth/discord/callback
   ```

   Add the production HTTPS callback as a separate redirect when the API has a public domain:

   ```text
   https://auth.example.com/v1/auth/oauth/discord/callback
   ```

7. Click **Save Changes**. A bot is not required. Horizon requests only the `identify` and `email` OAuth scopes.
8. Put the values in `backend/.env`:

   ```dotenv
   DISCORD_CLIENT_ID=the Application ID or Client ID from Discord
   DISCORD_CLIENT_SECRET=the Client Secret from Discord OAuth2
   DISCORD_REDIRECT_URI=http://127.0.0.1:8787/v1/auth/oauth/discord/callback
   ```

- `DISCORD_CLIENT_ID` is not secret.
- `DISCORD_CLIENT_SECRET` **is secret**.
- `DISCORD_REDIRECT_URI` is not secret and must exactly match Discord.

Discord's official flow is documented under [OAuth2](https://docs.discord.com/developers/topics/oauth2).

Restart `pnpm dev`, press Discord in Horizon, authorize in the system browser, and confirm the desktop app resumes at Onboarding or Dashboard.

## 4. Configure Epic Games / Epic Account Services

Epic's portal labels can change, but the values all come from the Horizon product's Epic Online Services settings.

1. Open the [Epic Games Developer Portal](https://dev.epicgames.com/portal) and sign in with an Epic account that has two-factor authentication enabled.
2. Create or select your organization, then create a product named `Horizon`.
3. Open the product's **Product Settings**. Create or select a **Sandbox**, then create a **Deployment** for development.
4. Open **Epic Account Services** for the product and choose **Configure**.
5. Create an Epic Account Services application for Horizon. Complete its brand name, privacy policy URL, and required consent-screen information.
6. Add the callback under the application's **Redirect URLs**:

   ```text
   http://127.0.0.1:8787/v1/auth/oauth/epic/callback
   ```

   Also add the production HTTPS callback later:

   ```text
   https://auth.example.com/v1/auth/oauth/epic/callback
   ```

7. In **Client Policies**, create a policy for the Horizon backend and allow the Epic Account Services basic-profile authentication permissions needed for sign-in. Do not enable unrelated game or store permissions.
8. In **Clients**, create or select the confidential backend client, attach that client policy, and copy its Client ID and Client Secret.
9. Return to **Product Settings → Sandboxes/Deployments** and copy the Deployment ID selected in step 3.
10. Put those values in `backend/.env`:

   ```dotenv
   EPIC_CLIENT_ID=the EOS Client ID
   EPIC_CLIENT_SECRET=the EOS Client Secret
   EPIC_DEPLOYMENT_ID=the development Deployment ID
   EPIC_REDIRECT_URI=http://127.0.0.1:8787/v1/auth/oauth/epic/callback
   ```

- `EPIC_CLIENT_ID` is not secret.
- `EPIC_CLIENT_SECRET` **is secret**.
- `EPIC_DEPLOYMENT_ID` is not secret.
- `EPIC_REDIRECT_URI` is not secret and must exactly match Epic.

Leave the `EPIC_AUTHORIZE_URL`, `EPIC_TOKEN_URL`, `EPIC_USERINFO_URL`, and `EPIC_SCOPES` example values unchanged unless Epic's documentation for your approved product explicitly gives different endpoints or scopes.

Epic's official background material is in [Introduction to Epic Online Services](https://onlineservices.epicgames.com/en-US/news/introduction-to-epic-online-services-eos) and the [Epic Developer Portal guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/getting-started-with-dev-portal-for-unreal-engine).

Restart `pnpm dev`, press Epic in Horizon, complete the Epic browser prompt, and confirm Horizon resumes in the desktop app. Epic may require brand/application review before accounts outside your organization can sign in.

## 5. Configure real email delivery

During development, `EMAIL_DELIVERY_MODE=console` deliberately avoids sending email. Reset and verification codes are returned only by the development API so the flows can be tested locally. Production refuses console mode.

For a concrete SMTP setup, SendGrid is one option:

1. Create an account at [Twilio SendGrid](https://signup.sendgrid.com/).
2. In SendGrid, open **Settings → Sender Authentication**. Authenticate your domain (recommended) or verify a single sender address for initial testing. Follow SendGrid's DNS instructions until it shows **Verified**.
3. Open [Settings → API Keys](https://app.sendgrid.com/settings/api_keys), choose **Create API Key**, name it `Horizon SMTP`, and grant only the mail-sending access Horizon needs.
4. Copy the API key once. It becomes the SMTP password.
5. Enter these values in `backend/.env`:

   ```dotenv
   EMAIL_DELIVERY_MODE=smtp
   EMAIL_FROM=Horizon <no-reply@your-verified-domain.example>
   SMTP_HOST=smtp.sendgrid.net
   SMTP_PORT=587
   SMTP_SECURE=false
   SMTP_USER=apikey
   SMTP_PASSWORD=the SendGrid API key
   ```

- `EMAIL_FROM` is not secret, but its address must be verified by the provider.
- `SMTP_HOST`, `SMTP_PORT`, `SMTP_SECURE`, and `SMTP_USER` are normally not secret.
- `SMTP_PASSWORD` **is secret**.

Port `587` begins as a normal SMTP connection and upgrades to TLS, so `SMTP_SECURE=false` is correct for Nodemailer on that port. If a different provider explicitly gives port `465`, use its values and normally set `SMTP_SECURE=true`.

These exact SendGrid values are documented in [Integrating with the SMTP API](https://www.twilio.com/docs/sendgrid/for-developers/sending-email/integrating-with-the-smtp-api). The TLS behavior is documented by [Nodemailer SMTP transport](https://nodemailer.com/smtp).

For production, set `REQUIRE_EMAIL_VERIFICATION=true` only after real delivery and the verification-code customer flow have both been tested. Restart the backend, create a new account using an inbox you control, and confirm the verification email arrives. Then use **Forgot Password** and confirm the reset email arrives. Check spam and the email provider's delivery/activity log if it does not.

## Every `.env` variable in plain English

### Server and database

| Variable | What to enter | Where it comes from | Secret? |
|---|---|---|---|
| `NODE_ENV` | `development` locally; `production` on the live server | You choose it | No |
| `HOST` | `127.0.0.1` locally; usually `0.0.0.0` in the backend container/server | You choose it | No |
| `PORT` | `8787` unless the server uses another free port | You choose it | No |
| `APP_BASE_URL` | `http://127.0.0.1:8787` locally; the public HTTPS API URL in production | Your backend domain/deployment | No |
| `DATABASE_URL` | Full PostgreSQL connection string | Local Docker values above, or the database host's **Connection details / Connection string** page | **Yes**, because it contains the database password |
| `DATABASE_SSL` | `false` for the included local Docker database; usually `true` when a hosted database requires TLS | Database provider's connection instructions | No |
| `TRUST_PROXY` | `false` locally; `true` only behind a trusted production reverse proxy such as Nginx | Your deployment design | No |

If PostgreSQL runs on the same Oracle Cloud VM, create a dedicated `horizon` database and user, keep port 5432 private, and use a connection string similar to `postgresql://USER:PASSWORD@127.0.0.1:5432/horizon`. If using a hosted PostgreSQL service, copy its PostgreSQL connection URI from its dashboard. Never use the example development password in production.

### Session timing

| Variable | Meaning | Recommended action | Secret? |
|---|---|---|---|
| `SESSION_ACCESS_MINUTES` | Minutes before a short desktop access credential is renewed | Leave `15` | No |
| `SESSION_REFRESH_DAYS` | Days a signed-in desktop session can be renewed | Leave `30` | No |
| `OAUTH_ATTEMPT_MINUTES` | Minutes allowed to finish provider browser sign-in | Leave `10` | No |
| `DESKTOP_CODE_SECONDS` | Seconds the one-time browser-to-desktop code remains valid | Leave `90` | No |

These values are generated/managed by Horizon. There is no JWT key or session secret for you to create in this backend.

### Email

| Variable | What to enter | Where it comes from | Secret? |
|---|---|---|---|
| `REQUIRE_EMAIL_VERIFICATION` | `false` for initial local testing; `true` after production delivery is tested | You choose it | No |
| `EMAIL_DELIVERY_MODE` | `console` locally; `smtp` in production | You choose it | No |
| `EMAIL_FROM` | Display name and verified sending address | Email provider's **Sender Authentication** page | No |
| `SMTP_HOST` | SMTP server hostname | Email provider's SMTP settings | No |
| `SMTP_PORT` | Usually `587`, or the port specified by the provider | Email provider's SMTP settings | No |
| `SMTP_SECURE` | `false` for port 587; normally `true` for port 465 | Email provider's SMTP settings | No |
| `SMTP_USER` | SMTP username | Email provider's SMTP credentials page | Usually no, but keep private |
| `SMTP_PASSWORD` | SMTP password or provider API key | Email provider's SMTP/API-key page | **Yes** |

### Provider sign-in

| Variable | What to enter | Dashboard location | Secret? |
|---|---|---|---|
| `GOOGLE_CLIENT_ID` | Google Web application Client ID | Google Auth Platform → Clients → Horizon backend | No |
| `GOOGLE_CLIENT_SECRET` | Matching Google Client secret | Same Google client details dialog | **Yes** |
| `GOOGLE_REDIRECT_URI` | Exact Google callback for this environment | Add the same value under Google client → Authorized redirect URIs | No |
| `DISCORD_CLIENT_ID` | Discord Application ID / Client ID | Discord Developer Portal → Horizon → OAuth2 | No |
| `DISCORD_CLIENT_SECRET` | Discord OAuth2 Client Secret | Same OAuth2 page | **Yes** |
| `DISCORD_REDIRECT_URI` | Exact Discord callback for this environment | Discord OAuth2 → Redirects | No |
| `EPIC_CLIENT_ID` | EOS confidential client ID | Epic product → Product Settings / Clients | No |
| `EPIC_CLIENT_SECRET` | Secret paired with that EOS client | Same Epic client page | **Yes** |
| `EPIC_DEPLOYMENT_ID` | EOS deployment used by Horizon | Epic product → Product Settings → Sandboxes/Deployments | No |
| `EPIC_REDIRECT_URI` | Exact Epic callback for this environment | Epic Account Services application → Redirect URLs | No |
| `EPIC_AUTHORIZE_URL` | Epic browser authorization endpoint | Already supplied by `.env.example` | No |
| `EPIC_TOKEN_URL` | Epic server token endpoint | Already supplied by `.env.example` | No |
| `EPIC_USERINFO_URL` | Epic account profile endpoint | Already supplied by `.env.example` | No |
| `EPIC_SCOPES` | Epic permissions requested; currently `basic_profile` | Already supplied by `.env.example` | No |

## Production checklist

- [ ] PostgreSQL database and dedicated user created
- [ ] `DATABASE_URL` replaced with the production connection string
- [ ] `NODE_ENV=production`
- [ ] Public API domain points to the Oracle Cloud server
- [ ] HTTPS certificate and reverse proxy configured
- [ ] `APP_BASE_URL` and all provider callback variables use that exact HTTPS domain
- [ ] Google production callback registered and sign-in tested
- [ ] Discord production callback registered and sign-in tested
- [ ] Epic application/client/deployment approved and sign-in tested
- [ ] Sending domain authenticated with the email provider
- [ ] SMTP verification and password-reset messages tested
- [ ] `REQUIRE_EMAIL_VERIFICATION=true` only after the complete verification flow works
- [ ] `backend/.env` remains absent from `git status` and GitHub

## Restart and test after any `.env` change

The backend reads `.env` only when it starts. Stop `pnpm dev` with **Ctrl+C**, then run it again:

```powershell
Set-Location backend
pnpm migrate
pnpm dev
```

Close and reopen Horizon after changing `HORIZON_API_BASE_URL`. Check `/health`, then test one provider button at a time. Provider sign-in must open the default system browser and return to Horizon; the provider's secret must never appear in the desktop app or its configuration.

## Your exact next step

Open the private file now:

```powershell
Set-Location "C:\Users\Ali Hassan\Documents\Codex\2026-08-13\https-github-com-lachlan1234564-horzion-labs\work\repo"
notepad backend\.env
```

Keep the local development values first, make sure `DATABASE_URL` matches the Docker value in section 1, start PostgreSQL/API, and complete **Create Account → Onboarding → Dashboard → Sign out → Sign in**. After that works, configure providers in this order: **Google, Discord, Epic, then SMTP**, restarting and testing after each one.
