export type EmailDeliveryMode = "console" | "smtp";

export interface AppConfig {
  nodeEnv: string;
  host: string;
  port: number;
  appBaseUrl: string;
  databaseUrl: string;
  databaseSsl: boolean;
  trustProxy: boolean;
  accessMinutes: number;
  refreshDays: number;
  oauthAttemptMinutes: number;
  desktopCodeSeconds: number;
  requireEmailVerification: boolean;
  emailMode: EmailDeliveryMode;
  emailFrom: string;
  smtp: { host?: string; port: number; secure: boolean; user?: string; password?: string };
  google: ProviderConfig;
  discord: ProviderConfig;
  epic: ProviderConfig & { deploymentId?: string };
}

export interface ProviderConfig {
  clientId?: string;
  clientSecret?: string;
  redirectUri?: string;
  authorizeUrl: string;
  tokenUrl: string;
  userInfoUrl: string;
  scopes: string;
}

const bool = (value: string | undefined, fallback = false) => value === undefined ? fallback : /^(1|true|yes)$/i.test(value);
const integer = (value: string | undefined, fallback: number) => {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
};
const clean = (value: string | undefined) => value?.trim() || undefined;

export function loadConfig(env: NodeJS.ProcessEnv = process.env): AppConfig {
  const nodeEnv = (env.NODE_ENV ?? "development").trim().toLowerCase();
  const configuredDatabaseUrl = clean(env.DATABASE_URL);
  if (nodeEnv === "production" && !configuredDatabaseUrl) throw new Error("Production requires DATABASE_URL.");
  const databaseUrl = configuredDatabaseUrl ?? "postgresql://horizon:horizon_dev_only@127.0.0.1:5432/horizon";
  const emailMode = (env.EMAIL_DELIVERY_MODE ?? "console").toLowerCase();
  if (emailMode !== "console" && emailMode !== "smtp") throw new Error("EMAIL_DELIVERY_MODE must be console or smtp.");
  if (nodeEnv === "production" && emailMode === "console") throw new Error("Production requires EMAIL_DELIVERY_MODE=smtp.");
  if (nodeEnv === "production" && !clean(env.EMAIL_FROM)) throw new Error("Production requires EMAIL_FROM.");

  const config: AppConfig = {
    nodeEnv,
    host: clean(env.HOST) ?? (nodeEnv === "production" ? "0.0.0.0" : "127.0.0.1"),
    port: integer(env.PORT, 8787),
    appBaseUrl: clean(env.APP_BASE_URL) ?? "http://127.0.0.1:8787",
    databaseUrl,
    databaseSsl: bool(env.DATABASE_SSL),
    trustProxy: bool(env.TRUST_PROXY),
    accessMinutes: integer(env.SESSION_ACCESS_MINUTES, 15),
    refreshDays: integer(env.SESSION_REFRESH_DAYS, 30),
    oauthAttemptMinutes: integer(env.OAUTH_ATTEMPT_MINUTES, 10),
    desktopCodeSeconds: integer(env.DESKTOP_CODE_SECONDS, 90),
    requireEmailVerification: bool(env.REQUIRE_EMAIL_VERIFICATION),
    emailMode,
    emailFrom: clean(env.EMAIL_FROM) ?? "Horizon <no-reply@example.com>",
    smtp: {
      host: clean(env.SMTP_HOST), port: integer(env.SMTP_PORT, 587), secure: bool(env.SMTP_SECURE),
      user: clean(env.SMTP_USER), password: clean(env.SMTP_PASSWORD)
    },
    google: {
      clientId: clean(env.GOOGLE_CLIENT_ID), clientSecret: clean(env.GOOGLE_CLIENT_SECRET), redirectUri: clean(env.GOOGLE_REDIRECT_URI),
      authorizeUrl: "https://accounts.google.com/o/oauth2/v2/auth", tokenUrl: "https://oauth2.googleapis.com/token",
      userInfoUrl: "https://openidconnect.googleapis.com/v1/userinfo", scopes: "openid email profile"
    },
    discord: {
      clientId: clean(env.DISCORD_CLIENT_ID), clientSecret: clean(env.DISCORD_CLIENT_SECRET), redirectUri: clean(env.DISCORD_REDIRECT_URI),
      authorizeUrl: "https://discord.com/oauth2/authorize", tokenUrl: "https://discord.com/api/oauth2/token",
      userInfoUrl: "https://discord.com/api/v10/users/@me", scopes: "identify email"
    },
    epic: {
      clientId: clean(env.EPIC_CLIENT_ID), clientSecret: clean(env.EPIC_CLIENT_SECRET), redirectUri: clean(env.EPIC_REDIRECT_URI),
      deploymentId: clean(env.EPIC_DEPLOYMENT_ID),
      authorizeUrl: clean(env.EPIC_AUTHORIZE_URL) ?? "https://www.epicgames.com/id/authorize",
      tokenUrl: clean(env.EPIC_TOKEN_URL) ?? "https://api.epicgames.dev/epic/oauth/v2/token",
      userInfoUrl: clean(env.EPIC_USERINFO_URL) ?? "https://api.epicgames.dev/epic/oauth/v2/userInfo",
      scopes: clean(env.EPIC_SCOPES) ?? "basic_profile"
    }
  };
  if (config.emailMode === "smtp" && (!config.smtp.host || !config.smtp.user || !config.smtp.password))
    throw new Error("SMTP_HOST, SMTP_USER, and SMTP_PASSWORD are required for smtp delivery.");
  return config;
}
