import { randomUUID } from "node:crypto";
import type { PoolClient, QueryResultRow } from "pg";
import type { AppConfig } from "./config.js";
import { hashPassword, hashToken, randomToken, validatePassword, verifyPassword } from "./crypto.js";
import type { Database } from "./db.js";
import { AppError, badRequest, unauthorized } from "./errors.js";
import type { Mailer } from "./mailer.js";
import type { OAuthProviderName, OAuthProviders, ProviderIdentity } from "./providers.js";

interface UserRow extends QueryResultRow {
  id: string; display_name: string; profile_picture_url: string | null; primary_email: string | null;
  email_verified_at: Date | null; onboarding_step: number; onboarding_completed: boolean; created_at: Date;
}
interface IdentityRow extends QueryResultRow { provider: string; user_id: string; }
interface AttemptRow extends QueryResultRow {
  id: string; client_state: string; desktop_redirect_uri: string; link_user_id: string | null;
}

export interface PublicUser {
  id: string; displayName: string; profilePictureUrl?: string; email?: string; emailVerified: boolean;
  providers: string[]; createdAt: string; onboardingStep: number; onboardingCompleted: boolean;
}
export interface SessionResult { accessToken: string; refreshToken: string; accessExpiresAt: string; user: PublicUser; isNewAccount: boolean; }

export class AuthService {
  constructor(private readonly db: Database, private readonly config: AppConfig,
    private readonly mailer: Mailer, private readonly providers: OAuthProviders) {}

  providerStatuses() {
    return this.providers.names().map(id => ({ id, ...this.providers.availability(id) }));
  }

  async health(): Promise<void> { await this.db.query("SELECT 1"); }

  async register(input: { email?: unknown; password?: unknown; displayName?: unknown; deviceName?: unknown; userAgent?: string }): Promise<SessionResult & { developmentVerificationToken?: string }> {
    const email = this.email(input.email);
    const password = this.text(input.password, "Password", 256);
    const displayName = this.text(input.displayName, "Display name", 80);
    validatePassword(password);
    const normalized = email.toLowerCase();
    const passwordHash = await hashPassword(password);
    const verificationToken = randomToken(24);
    let userId = "";
    try {
      await this.db.transaction(async client => {
        const existing = await client.query("SELECT 1 FROM users WHERE primary_email_normalized=$1", [normalized]);
        if (existing.rowCount) throw new AppError(409, "EMAIL_ALREADY_REGISTERED", "An account already exists for that email address.");
        userId = randomUUID();
        await client.query(`INSERT INTO users(id,display_name,primary_email,primary_email_normalized,email_verified_at)
          VALUES($1,$2,$3,$4,$5)`, [userId, displayName, email, normalized, this.config.requireEmailVerification ? null : new Date()]);
        await client.query(`INSERT INTO auth_identities(id,user_id,provider,provider_account_id,provider_email,provider_display_name)
          VALUES($1,$2,'email',$3,$4,$5)`, [randomUUID(), userId, normalized, email, displayName]);
        await client.query("INSERT INTO password_credentials(user_id,password_hash) VALUES($1,$2)", [userId, passwordHash]);
        await this.createAccountToken(client, userId, "email_verification", verificationToken, 24 * 60);
      });
    } catch (error) {
      if (this.databaseErrorCode(error) === "23505")
        throw new AppError(409, "EMAIL_ALREADY_REGISTERED", "An account already exists for that email address.");
      throw error;
    }
    await this.mailer.sendVerification(email, verificationToken);
    const result = await this.createSession(userId, String(input.deviceName ?? "Horizon Windows"), input.userAgent, true);
    if (this.developmentEmailTokensEnabled())
      return { ...result, developmentVerificationToken: verificationToken };
    return result;
  }

  async login(input: { email?: unknown; password?: unknown; deviceName?: unknown; userAgent?: string }): Promise<SessionResult> {
    const email = this.email(input.email).toLowerCase();
    const password = this.text(input.password, "Password", 256);
    const result = await this.db.query<UserRow & { password_hash: string }>(`SELECT u.*,p.password_hash FROM users u
      JOIN password_credentials p ON p.user_id=u.id WHERE u.primary_email_normalized=$1`, [email]);
    const user = result.rows[0];
    if (!user || !await verifyPassword(password, user.password_hash))
      throw unauthorized("The email address or password is incorrect.");
    if (this.config.requireEmailVerification && !user.email_verified_at)
      throw new AppError(403, "EMAIL_NOT_VERIFIED", "Verify your email address before signing in.");
    await this.db.query("UPDATE auth_identities SET last_used_at=NOW() WHERE user_id=$1 AND provider='email'", [user.id]);
    return this.createSession(user.id, String(input.deviceName ?? "Horizon Windows"), input.userAgent, false);
  }

  async refresh(refreshToken: unknown, deviceName?: unknown, userAgent?: string): Promise<SessionResult> {
    const token = this.text(refreshToken, "Refresh token", 1024);
    const tokenHash = hashToken(token);
    let userId = "";
    await this.db.transaction(async client => {
      const result = await client.query<{ id: string; user_id: string }>(`SELECT id,user_id FROM sessions
        WHERE refresh_token_hash=$1 AND revoked_at IS NULL AND refresh_expires_at>NOW() FOR UPDATE`, [tokenHash]);
      const session = result.rows[0];
      if (!session) throw unauthorized("Your Horizon session has expired. Sign in again.");
      userId = session.user_id;
      await client.query("UPDATE sessions SET revoked_at=NOW() WHERE id=$1", [session.id]);
    });
    return this.createSession(userId, String(deviceName ?? "Horizon Windows"), userAgent, false);
  }

  async logout(refreshToken: unknown): Promise<void> {
    if (typeof refreshToken !== "string" || !refreshToken) return;
    await this.db.query("UPDATE sessions SET revoked_at=NOW() WHERE refresh_token_hash=$1 AND revoked_at IS NULL", [hashToken(refreshToken)]);
  }

  async authenticate(accessToken: string | undefined): Promise<PublicUser> {
    if (!accessToken) throw unauthorized();
    const result = await this.db.query<{ user_id: string }>(`UPDATE sessions SET last_seen_at=NOW()
      WHERE access_token_hash=$1 AND revoked_at IS NULL AND access_expires_at>NOW() RETURNING user_id`, [hashToken(accessToken)]);
    const row = result.rows[0];
    if (!row) throw unauthorized("Your Horizon session has expired.");
    return this.publicUser(row.user_id);
  }

  async updateOnboarding(userId: string, input: { step?: unknown; completed?: unknown }): Promise<PublicUser> {
    const step = Number(input.step ?? 0);
    if (!Number.isInteger(step) || step < 0 || step > 100) throw badRequest("INVALID_ONBOARDING_STEP", "Onboarding step is invalid.");
    const completed = input.completed === true;
    await this.db.query("UPDATE users SET onboarding_step=$2,onboarding_completed=$3,updated_at=NOW() WHERE id=$1", [userId, step, completed]);
    return this.publicUser(userId);
  }

  async updateProfile(userId: string, input: { displayName?: unknown; profilePictureUrl?: unknown }): Promise<PublicUser> {
    const displayName = this.text(input.displayName, "Display name", 80);
    let profilePictureUrl: string | null = null;
    if (typeof input.profilePictureUrl === "string" && input.profilePictureUrl.trim()) {
      try {
        const url = new URL(input.profilePictureUrl.trim());
        if (url.protocol !== "https:") throw new Error();
        profilePictureUrl = url.toString();
      } catch { throw badRequest("VALIDATION_ERROR", "Profile picture must be a valid HTTPS URL."); }
    }
    await this.db.query("UPDATE users SET display_name=$2,profile_picture_url=$3,updated_at=NOW() WHERE id=$1", [userId, displayName, profilePictureUrl]);
    return this.publicUser(userId);
  }

  async requestPasswordReset(rawEmail: unknown): Promise<{ developmentResetToken?: string }> {
    const email = this.email(rawEmail).toLowerCase();
    const result = await this.db.query<UserRow>(`SELECT u.* FROM users u
      JOIN password_credentials p ON p.user_id=u.id WHERE u.primary_email_normalized=$1`, [email]);
    const user = result.rows[0];
    if (!user) return {};
    const token = randomToken(24);
    await this.db.transaction(async client => {
      await client.query("UPDATE account_tokens SET consumed_at=NOW() WHERE user_id=$1 AND purpose='password_reset' AND consumed_at IS NULL", [user.id]);
      await this.createAccountToken(client, user.id, "password_reset", token, 30);
    });
    await this.mailer.sendPasswordReset(user.primary_email!, token);
    return this.developmentEmailTokensEnabled() ? { developmentResetToken: token } : {};
  }

  async resetPassword(tokenValue: unknown, passwordValue: unknown): Promise<void> {
    const token = this.text(tokenValue, "Reset code", 1024);
    const password = this.text(passwordValue, "Password", 256);
    validatePassword(password);
    const passwordHash = await hashPassword(password);
    await this.db.transaction(async client => {
      const result = await client.query<{ id: string; user_id: string }>(`SELECT id,user_id FROM account_tokens
        WHERE purpose='password_reset' AND token_hash=$1 AND consumed_at IS NULL AND expires_at>NOW() FOR UPDATE`, [hashToken(token)]);
      const row = result.rows[0];
      if (!row) throw badRequest("RESET_TOKEN_INVALID", "That reset code is invalid or has expired.");
      await client.query("UPDATE account_tokens SET consumed_at=NOW() WHERE id=$1", [row.id]);
      await client.query("UPDATE password_credentials SET password_hash=$2,password_changed_at=NOW() WHERE user_id=$1", [row.user_id, passwordHash]);
      await client.query("UPDATE sessions SET revoked_at=NOW() WHERE user_id=$1 AND revoked_at IS NULL", [row.user_id]);
    });
  }

  async verifyEmail(tokenValue: unknown): Promise<void> {
    const token = this.text(tokenValue, "Verification code", 1024);
    await this.db.transaction(async client => {
      const result = await client.query<{ id: string; user_id: string }>(`SELECT id,user_id FROM account_tokens
        WHERE purpose='email_verification' AND token_hash=$1 AND consumed_at IS NULL AND expires_at>NOW() FOR UPDATE`, [hashToken(token)]);
      const row = result.rows[0];
      if (!row) throw badRequest("VERIFICATION_TOKEN_INVALID", "That verification code is invalid or has expired.");
      await client.query("UPDATE account_tokens SET consumed_at=NOW() WHERE id=$1", [row.id]);
      await client.query("UPDATE users SET email_verified_at=COALESCE(email_verified_at,NOW()),updated_at=NOW() WHERE id=$1", [row.user_id]);
    });
  }

  async startOAuth(provider: OAuthProviderName, desktopRedirectValue: unknown, clientStateValue: unknown, linkUserId?: string) {
    const desktopRedirectUri = this.desktopRedirect(desktopRedirectValue);
    const clientState = this.text(clientStateValue, "Sign-in request", 256);
    const state = randomToken(32);
    const authorizationUrl = this.providers.authorizationUrl(provider, state);
    await this.db.query(`INSERT INTO oauth_attempts(id,provider,state_hash,client_state,desktop_redirect_uri,link_user_id,expires_at)
      VALUES($1,$2,$3,$4,$5,$6,NOW()+($7*INTERVAL '1 minute'))`,
      [randomUUID(), provider, hashToken(state), clientState, desktopRedirectUri, linkUserId ?? null, this.config.oauthAttemptMinutes]);
    return { authorizationUrl, expiresInSeconds: this.config.oauthAttemptMinutes * 60 };
  }

  async completeOAuth(provider: OAuthProviderName, stateValue: unknown, codeValue: unknown): Promise<{ redirectUri: string }> {
    const state = this.text(stateValue, "Sign-in request", 1024);
    const code = this.text(codeValue, "Sign-in response", 4096);
    const attemptResult = await this.db.query<AttemptRow>(`UPDATE oauth_attempts SET consumed_at=NOW()
      WHERE provider=$1 AND state_hash=$2 AND consumed_at IS NULL AND expires_at>NOW()
      RETURNING id,client_state,desktop_redirect_uri,link_user_id`, [provider, hashToken(state)]);
    const attempt = attemptResult.rows[0];
    if (!attempt) throw badRequest("OAUTH_STATE_INVALID", "This sign-in request has expired. Please try again.");
    try {
      const identity = await this.providers.exchangeCode(provider, code);
      const account = await this.findOrCreateProviderAccount(provider, identity, attempt.link_user_id);
      const desktopCode = randomToken(32);
      await this.db.query(`INSERT INTO desktop_authorization_codes(id,user_id,code_hash,desktop_redirect_uri,is_new_account,expires_at)
        VALUES($1,$2,$3,$4,$5,NOW()+($6*INTERVAL '1 second'))`,
        [randomUUID(), account.userId, hashToken(desktopCode), attempt.desktop_redirect_uri, account.isNew, this.config.desktopCodeSeconds]);
      const redirect = new URL(attempt.desktop_redirect_uri);
      redirect.searchParams.set("code", desktopCode);
      redirect.searchParams.set("state", attempt.client_state);
      redirect.searchParams.set("new_account", String(account.isNew));
      return { redirectUri: redirect.toString() };
    } catch (error) {
      if (!(error instanceof AppError)) console.error("Unexpected provider sign-in failure", error);
      const redirect = new URL(attempt.desktop_redirect_uri);
      redirect.searchParams.set("state", attempt.client_state);
      redirect.searchParams.set("error", error instanceof AppError ? error.code : "PROVIDER_SIGN_IN_FAILED");
      redirect.searchParams.set("error_description", error instanceof AppError ? error.message : "Horizon could not finish sign-in. Please try again.");
      return { redirectUri: redirect.toString() };
    }
  }

  async oauthDenied(provider: OAuthProviderName, stateValue: unknown, reason: string): Promise<string> {
    const state = this.text(stateValue, "Sign-in request", 1024);
    const result = await this.db.query<AttemptRow>(`UPDATE oauth_attempts SET consumed_at=NOW()
      WHERE provider=$1 AND state_hash=$2 AND consumed_at IS NULL AND expires_at>NOW()
      RETURNING client_state,desktop_redirect_uri`, [provider, hashToken(state)]);
    const attempt = result.rows[0];
    if (!attempt) throw badRequest("OAUTH_STATE_INVALID", "This sign-in request has expired. Please try again.");
    const redirect = new URL(attempt.desktop_redirect_uri);
    redirect.searchParams.set("state", attempt.client_state);
    redirect.searchParams.set("error", reason === "access_denied" ? "CANCELLED" : "PROVIDER_SIGN_IN_FAILED");
    redirect.searchParams.set("error_description", reason === "access_denied" ? "Sign-in was cancelled." : "Horizon could not finish sign-in. Please try again.");
    return redirect.toString();
  }

  async exchangeDesktopCode(codeValue: unknown, redirectValue: unknown, deviceName?: unknown, userAgent?: string): Promise<SessionResult> {
    const code = this.text(codeValue, "Sign-in code", 1024);
    const redirect = this.desktopRedirect(redirectValue);
    let userId = "";
    let isNewAccount = false;
    await this.db.transaction(async client => {
      const result = await client.query<{ id: string; user_id: string; is_new_account: boolean }>(`SELECT id,user_id,is_new_account FROM desktop_authorization_codes
        WHERE code_hash=$1 AND desktop_redirect_uri=$2 AND consumed_at IS NULL AND expires_at>NOW() FOR UPDATE`, [hashToken(code), redirect]);
      const row = result.rows[0];
      if (!row) throw badRequest("DESKTOP_CODE_INVALID", "This sign-in request is invalid or has expired. Please try again.");
      userId = row.user_id;
      isNewAccount = row.is_new_account;
      await client.query("UPDATE desktop_authorization_codes SET consumed_at=NOW() WHERE id=$1", [row.id]);
    });
    return this.createSession(userId, String(deviceName ?? "Horizon Windows"), userAgent, isNewAccount);
  }

  private async findOrCreateProviderAccount(provider: OAuthProviderName, identity: ProviderIdentity, linkUserId: string | null): Promise<{ userId: string; isNew: boolean }> {
    return this.db.transaction(async client => {
      const existing = await client.query<IdentityRow>("SELECT user_id,provider FROM auth_identities WHERE provider=$1 AND provider_account_id=$2 FOR UPDATE", [provider, identity.accountId]);
      if (linkUserId) {
        if (existing.rows[0] && existing.rows[0].user_id !== linkUserId)
          throw new AppError(409, "PROVIDER_ALREADY_LINKED", `That ${this.providerName(provider)} account is already connected to another Horizon account.`);
        const other = await client.query("SELECT 1 FROM auth_identities WHERE user_id=$1 AND provider=$2", [linkUserId, provider]);
        if (other.rowCount && !existing.rows[0])
          throw new AppError(409, "PROVIDER_ALREADY_LINKED", `This Horizon account is already connected to ${this.providerName(provider)}.`);
        if (!existing.rows[0]) {
          await this.insertIdentity(client, linkUserId, provider, identity);
        } else {
          await client.query(`UPDATE auth_identities SET provider_email=$3,provider_display_name=$4,provider_avatar_url=$5,last_used_at=NOW()
            WHERE provider=$1 AND provider_account_id=$2`, [provider, identity.accountId, identity.email ?? null, identity.displayName, identity.avatarUrl ?? null]);
        }
        return { userId: linkUserId, isNew: false };
      }
      if (existing.rows[0]) {
        await client.query(`UPDATE auth_identities SET provider_email=$3,provider_display_name=$4,provider_avatar_url=$5,last_used_at=NOW()
          WHERE provider=$1 AND provider_account_id=$2`, [provider, identity.accountId, identity.email ?? null, identity.displayName, identity.avatarUrl ?? null]);
        return { userId: existing.rows[0].user_id, isNew: false };
      }
      if (identity.email) {
        const collision = await client.query("SELECT 1 FROM users WHERE primary_email_normalized=$1", [identity.email.toLowerCase()]);
        if (collision.rowCount) throw new AppError(409, "ACCOUNT_LINK_REQUIRED", `An account already uses this email. Sign in to that Horizon account and connect ${this.providerName(provider)} from Profile.`);
      }
      const userId = randomUUID();
      await client.query(`INSERT INTO users(id,display_name,profile_picture_url,primary_email,primary_email_normalized,email_verified_at)
        VALUES($1,$2,$3,$4,$5,$6)`, [userId, identity.displayName, identity.avatarUrl ?? null, identity.email ?? null,
        identity.email?.toLowerCase() ?? null, identity.emailVerified && identity.email ? new Date() : null]);
      await this.insertIdentity(client, userId, provider, identity);
      return { userId, isNew: true };
    });
  }

  private async insertIdentity(client: PoolClient, userId: string, provider: OAuthProviderName, identity: ProviderIdentity) {
    await client.query(`INSERT INTO auth_identities(id,user_id,provider,provider_account_id,provider_email,provider_display_name,provider_avatar_url)
      VALUES($1,$2,$3,$4,$5,$6,$7)`, [randomUUID(), userId, provider, identity.accountId, identity.email ?? null, identity.displayName, identity.avatarUrl ?? null]);
  }

  private async createSession(userId: string, deviceName: string, userAgent: string | undefined, isNewAccount: boolean): Promise<SessionResult> {
    const accessToken = randomToken(32), refreshToken = randomToken(48), sessionId = randomUUID();
    const accessExpiresAt = new Date(Date.now() + this.config.accessMinutes * 60_000);
    const refreshExpiresAt = new Date(Date.now() + this.config.refreshDays * 86_400_000);
    await this.db.query(`INSERT INTO sessions(id,user_id,access_token_hash,refresh_token_hash,access_expires_at,refresh_expires_at,device_name,user_agent)
      VALUES($1,$2,$3,$4,$5,$6,$7,$8)`, [sessionId, userId, hashToken(accessToken), hashToken(refreshToken), accessExpiresAt, refreshExpiresAt, deviceName.slice(0, 200), userAgent?.slice(0, 500) ?? null]);
    return { accessToken, refreshToken, accessExpiresAt: accessExpiresAt.toISOString(), user: await this.publicUser(userId), isNewAccount };
  }

  private async publicUser(userId: string): Promise<PublicUser> {
    const [userResult, identities] = await Promise.all([
      this.db.query<UserRow>("SELECT * FROM users WHERE id=$1", [userId]),
      this.db.query<IdentityRow>("SELECT provider,user_id FROM auth_identities WHERE user_id=$1 ORDER BY linked_at", [userId])
    ]);
    const user = userResult.rows[0];
    if (!user) throw unauthorized();
    return { id: user.id, displayName: user.display_name, profilePictureUrl: user.profile_picture_url ?? undefined,
      email: user.primary_email ?? undefined, emailVerified: Boolean(user.email_verified_at), providers: identities.rows.map(row => row.provider),
      createdAt: user.created_at.toISOString(), onboardingStep: user.onboarding_step, onboardingCompleted: user.onboarding_completed };
  }

  private async createAccountToken(client: PoolClient, userId: string, purpose: string, token: string, minutes: number) {
    await client.query(`INSERT INTO account_tokens(id,user_id,purpose,token_hash,expires_at)
      VALUES($1,$2,$3,$4,NOW()+($5*INTERVAL '1 minute'))`, [randomUUID(), userId, purpose, hashToken(token), minutes]);
  }

  private text(value: unknown, name: string, maxLength: number): string {
    if (typeof value !== "string" || !value.trim()) throw badRequest("VALIDATION_ERROR", `${name} is required.`);
    const text = value.trim();
    if (text.length > maxLength) throw badRequest("VALIDATION_ERROR", `${name} is too long.`);
    return text;
  }
  private email(value: unknown): string {
    const email = this.text(value, "Email", 254);
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) throw badRequest("VALIDATION_ERROR", "Enter a valid email address.");
    return email;
  }
  private providerName(provider: OAuthProviderName): string {
    return provider === "epic" ? "Epic Games" : provider[0]!.toUpperCase() + provider.slice(1);
  }
  private databaseErrorCode(error: unknown): string | undefined {
    if (typeof error !== "object" || error === null || !("code" in error)) return undefined;
    return typeof error.code === "string" ? error.code : undefined;
  }
  private developmentEmailTokensEnabled(): boolean {
    return this.config.emailMode === "console" && (this.config.nodeEnv === "development" || this.config.nodeEnv === "test");
  }
  private desktopRedirect(value: unknown): string {
    const raw = this.text(value, "Sign-in return address", 512);
    let url: URL;
    try { url = new URL(raw); } catch { throw badRequest("INVALID_DESKTOP_REDIRECT", "Horizon couldn't prepare sign-in. Please try again."); }
    if (url.protocol !== "http:" || url.hostname !== "127.0.0.1" || !url.port || url.username || url.password || url.hash || url.pathname !== "/callback")
      throw badRequest("INVALID_DESKTOP_REDIRECT", "Horizon couldn't prepare sign-in. Please try again.");
    return url.toString();
  }
}
