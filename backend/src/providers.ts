import type { AppConfig, ProviderConfig } from "./config.js";
import { badRequest, unavailable } from "./errors.js";

export type OAuthProviderName = "google" | "discord" | "epic";

export interface ProviderIdentity {
  accountId: string;
  email?: string;
  emailVerified: boolean;
  displayName: string;
  avatarUrl?: string;
}

interface TokenPayload {
  access_token?: string;
  account_id?: string;
  displayName?: string;
  [key: string]: unknown;
}

export class OAuthProviders {
  constructor(private readonly config: AppConfig) {}

  names(): OAuthProviderName[] { return ["google", "discord", "epic"]; }

  isProvider(value: string): value is OAuthProviderName {
    return this.names().includes(value as OAuthProviderName);
  }

  availability(provider: OAuthProviderName): { available: boolean; message?: string } {
    const config = this.providerConfig(provider);
    const configured = Boolean(config.clientId && config.clientSecret && config.redirectUri &&
      (provider !== "epic" || this.config.epic.deploymentId));
    return configured
      ? { available: true }
      : { available: false, message: `${this.label(provider)} sign-in is currently unavailable.` };
  }

  authorizationUrl(provider: OAuthProviderName, state: string): string {
    this.assertAvailable(provider);
    const config = this.providerConfig(provider);
    const url = new URL(config.authorizeUrl);
    url.searchParams.set("client_id", config.clientId!);
    url.searchParams.set("redirect_uri", config.redirectUri!);
    url.searchParams.set("response_type", "code");
    url.searchParams.set("scope", config.scopes);
    url.searchParams.set("state", state);
    if (provider === "google") {
      url.searchParams.set("prompt", "select_account");
    }
    if (provider === "epic" && this.config.epic.deploymentId)
      url.searchParams.set("deployment_id", this.config.epic.deploymentId);
    return url.toString();
  }

  async exchangeCode(provider: OAuthProviderName, code: string): Promise<ProviderIdentity> {
    this.assertAvailable(provider);
    const config = this.providerConfig(provider);
    const body = new URLSearchParams({
      grant_type: "authorization_code",
      code,
      redirect_uri: config.redirectUri!
    });
    const headers: Record<string, string> = { "content-type": "application/x-www-form-urlencoded", accept: "application/json" };
    if (provider === "epic") {
      headers.authorization = `Basic ${Buffer.from(`${config.clientId}:${config.clientSecret}`).toString("base64")}`;
      if (this.config.epic.deploymentId) body.set("deployment_id", this.config.epic.deploymentId);
    } else {
      body.set("client_id", config.clientId!);
      body.set("client_secret", config.clientSecret!);
    }

    const tokenResponse = await fetch(config.tokenUrl, { method: "POST", headers, body });
    const tokenPayload = await this.readJson<TokenPayload>(tokenResponse);
    if (!tokenResponse.ok || !tokenPayload.access_token)
      throw badRequest("PROVIDER_TOKEN_EXCHANGE_FAILED", `${this.label(provider)} couldn't complete sign-in. Please try again.`);

    const userResponse = await fetch(config.userInfoUrl, {
      headers: { authorization: `Bearer ${tokenPayload.access_token}`, accept: "application/json" }
    });
    const user = await this.readJson<Record<string, unknown>>(userResponse);
    if (!userResponse.ok)
      throw badRequest("PROVIDER_PROFILE_FAILED", `Horizon couldn't load your ${this.label(provider)} account. Please try again.`);
    return this.mapIdentity(provider, user, tokenPayload);
  }

  private mapIdentity(provider: OAuthProviderName, user: Record<string, unknown>, token: TokenPayload): ProviderIdentity {
    const string = (...values: unknown[]) => values.find(value => typeof value === "string" && value.length > 0) as string | undefined;
    const accountId = string(user.sub, user.id, user.account_id, token.account_id);
    if (!accountId) throw badRequest("PROVIDER_PROFILE_INVALID", `Horizon couldn't read your ${this.label(provider)} account. Please try again.`);
    const email = string(user.email);
    const displayName = string(user.name, user.global_name, user.displayName, user.preferred_username, user.username, token.displayName) ?? `${this.label(provider)} user`;
    let avatarUrl = string(user.picture, user.avatar_url);
    if (provider === "discord" && !avatarUrl && typeof user.avatar === "string")
      avatarUrl = `https://cdn.discordapp.com/avatars/${accountId}/${user.avatar}.png`;
    const verifiedValue = user.email_verified ?? user.verified;
    return { accountId, email, emailVerified: verifiedValue === true || verifiedValue === "true", displayName, avatarUrl };
  }

  private providerConfig(provider: OAuthProviderName): ProviderConfig { return this.config[provider]; }

  private assertAvailable(provider: OAuthProviderName): void {
    const status = this.availability(provider);
    if (!status.available) throw unavailable("PROVIDER_UNAVAILABLE", status.message!);
  }

  private label(provider: OAuthProviderName): string {
    return provider === "epic" ? "Epic Games" : provider[0]!.toUpperCase() + provider.slice(1);
  }

  private async readJson<T>(response: Response): Promise<T> {
    try { return await response.json() as T; }
    catch { return {} as T; }
  }
}
