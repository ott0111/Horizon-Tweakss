import assert from "node:assert/strict";
import test from "node:test";
import { loadConfig } from "../src/config.js";
import { OAuthProviders } from "../src/providers.js";

test("unconfigured providers report unavailable without throwing", () => {
  const providers = new OAuthProviders(loadConfig({ NODE_ENV: "test" }));
  assert.deepEqual(providers.availability("google"), { available: false, message: "Google sign-in is currently unavailable." });
  assert.equal(providers.availability("discord").available, false);
  assert.equal(providers.availability("epic").available, false);
});

test("production refuses development console email delivery", () => {
  assert.throws(() => loadConfig({ NODE_ENV: "production", DATABASE_URL: "postgresql://example.invalid/horizon", EMAIL_DELIVERY_MODE: "console" }), /requires EMAIL_DELIVERY_MODE=smtp/);
});

test("development binds to loopback while production defaults to all interfaces", () => {
  assert.equal(loadConfig({ NODE_ENV: "development" }).host, "127.0.0.1");
  assert.equal(loadConfig({ NODE_ENV: "production", DATABASE_URL: "postgresql://example.invalid/horizon", EMAIL_DELIVERY_MODE: "smtp", EMAIL_FROM: "Horizon <no-reply@example.com>", SMTP_HOST: "smtp.example.com", SMTP_USER: "user", SMTP_PASSWORD: "password" }).host, "0.0.0.0");
});

test("production requires explicit database and sender configuration", () => {
  assert.throws(() => loadConfig({ NODE_ENV: "PRODUCTION" }), /requires DATABASE_URL/);
  assert.throws(() => loadConfig({ NODE_ENV: "production", DATABASE_URL: "postgresql://example.invalid/horizon", EMAIL_DELIVERY_MODE: "smtp", SMTP_HOST: "smtp.example.com", SMTP_USER: "user", SMTP_PASSWORD: "password" }), /requires EMAIL_FROM/);
});

test("desktop authorization URL contains server callback and state", () => {
  const config = loadConfig({ NODE_ENV: "test", GOOGLE_CLIENT_ID: "client", GOOGLE_CLIENT_SECRET: "secret", GOOGLE_REDIRECT_URI: "https://api.example.com/v1/auth/oauth/google/callback" });
  const url = new URL(new OAuthProviders(config).authorizationUrl("google", "state-value"));
  assert.equal(url.searchParams.get("client_secret"), null);
  assert.equal(url.searchParams.get("access_type"), null);
  assert.equal(url.searchParams.get("state"), "state-value");
  assert.equal(url.searchParams.get("redirect_uri"), "https://api.example.com/v1/auth/oauth/google/callback");
});
