import "dotenv/config";
import assert from "node:assert/strict";
import { createHash, randomBytes, randomUUID } from "node:crypto";
import pg from "pg";

const base = process.env.HORIZON_INTEGRATION_BASE_URL ?? "http://127.0.0.1:58787";
const marker = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
const email = `integration-${marker}@example.test`;
const firstPassword = "Horizon-test-password-1";
const secondPassword = "Horizon-test-password-2";
const database = new pg.Pool({
  connectionString: process.env.DATABASE_URL ?? "postgresql://horizon:horizon_dev_only@127.0.0.1:5432/horizon",
  ssl: /^(1|true|yes)$/i.test(process.env.DATABASE_SSL ?? "") ? { rejectUnauthorized: true } : false
});
let createdUserId;

async function request(path, { method = "GET", body, token, expected = 200 } = {}) {
  const response = await fetch(`${base}${path}`, {
    method,
    headers: { ...(body ? { "content-type": "application/json" } : {}), ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined
  });
  const payload = response.status === 204 ? null : await response.json();
  assert.equal(response.status, expected, `${method} ${path}: ${JSON.stringify(payload)}`);
  return payload?.data ?? payload?.error;
}

try {
const providers = await request("/v1/auth/providers");
assert.deepEqual(providers.map(provider => provider.id), ["google", "discord", "epic"]);
for (const provider of providers) {
  assert.equal(typeof provider.available, "boolean");
  if (!provider.available) assert.equal(provider.message, `${provider.id === "epic" ? "Epic Games" : provider.id[0].toUpperCase() + provider.id.slice(1)} sign-in is currently unavailable.`);
}

const malformed = await fetch(`${base}/v1/auth/login`, { method: "POST", headers: { "content-type": "application/json" }, body: "{" });
assert.equal(malformed.status, 400);
assert.equal((await malformed.json()).error.code, "MALFORMED_JSON");

await request("/v1/auth/register", {
  method: "POST", expected: 400,
  body: { displayName: "Short Password", email: `short-${email}`, password: "too-short" }
});

const registration = await request("/v1/auth/register", {
  method: "POST", expected: 201,
  body: { displayName: "Integration User", email, password: firstPassword, deviceName: "Horizon Integration Test" }
});
assert.equal(registration.isNewAccount, true);
assert.equal(registration.user.providers.includes("email"), true);
assert.equal(typeof registration.developmentVerificationToken, "string");
createdUserId = registration.user.id;

await request("/v1/auth/register", {
  method: "POST", expected: 409,
  body: { displayName: "Duplicate User", email, password: firstPassword }
});

await request("/v1/auth/verify-email", { method: "POST", body: { token: registration.developmentVerificationToken } });
await request("/v1/auth/verify-email", { method: "POST", expected: 400, body: { token: registration.developmentVerificationToken } });
const current = await request("/v1/auth/me", { token: registration.accessToken });
assert.equal(current.email, email);

const profile = await request("/v1/account/profile", {
  method: "PATCH", token: registration.accessToken, body: { displayName: "Updated Integration User", profilePictureUrl: "https://example.com/avatar.png" }
});
assert.equal(profile.displayName, "Updated Integration User");
assert.equal(profile.profilePictureUrl, "https://example.com/avatar.png");

const onboarding = await request("/v1/account/onboarding", {
  method: "PUT", token: registration.accessToken, body: { step: 5, completed: true }
});
assert.equal(onboarding.onboardingCompleted, true);

await request("/v1/account/link/google/start", {
  method: "POST", expected: 401,
  body: { desktopRedirectUri: "http://127.0.0.1:43123/callback", clientState: "integration-state" }
});
const unavailableProvider = providers.find(provider => !provider.available);
if (unavailableProvider) {
  const before = await database.query("SELECT COUNT(*)::int AS count FROM oauth_attempts WHERE provider=$1", [unavailableProvider.id]);
  const unavailable = await request(`/v1/auth/oauth/${unavailableProvider.id}/start`, {
    method: "POST", expected: 503,
    body: { desktopRedirectUri: "http://127.0.0.1:43123/callback", clientState: "integration-state" }
  });
  assert.equal(unavailable.code, "PROVIDER_UNAVAILABLE");
  assert.match(unavailable.message, /currently unavailable/i);
  const after = await database.query("SELECT COUNT(*)::int AS count FROM oauth_attempts WHERE provider=$1", [unavailableProvider.id]);
  assert.equal(after.rows[0].count, before.rows[0].count);
}

const desktopCode = randomBytes(32).toString("base64url");
const desktopCodeHash = createHash("sha256").update(desktopCode, "utf8").digest("hex");
const desktopRedirectUri = "http://127.0.0.1:43123/callback";
const insertedCode = await database.query(`INSERT INTO desktop_authorization_codes
  (id,user_id,code_hash,desktop_redirect_uri,is_new_account,expires_at)
  VALUES($1,$2,$3,$4,FALSE,NOW()+INTERVAL '90 seconds') RETURNING code_hash`,
  [randomUUID(), registration.user.id, desktopCodeHash, desktopRedirectUri]);
assert.equal(insertedCode.rows[0].code_hash, desktopCodeHash);
assert.notEqual(insertedCode.rows[0].code_hash, desktopCode);
await request("/v1/auth/desktop/exchange", {
  method: "POST", expected: 400,
  body: { code: desktopCode, redirectUri: "http://127.0.0.1:43124/callback", deviceName: "Horizon Integration Test" }
});
const desktopSession = await request("/v1/auth/desktop/exchange", {
  method: "POST", body: { code: desktopCode, redirectUri: desktopRedirectUri, deviceName: "Horizon Integration Test" }
});
assert.equal(desktopSession.user.id, registration.user.id);
await request("/v1/auth/desktop/exchange", {
  method: "POST", expected: 400,
  body: { code: desktopCode, redirectUri: desktopRedirectUri, deviceName: "Horizon Integration Test" }
});

const refreshed = await request("/v1/auth/refresh", {
  method: "POST", body: { refreshToken: registration.refreshToken, deviceName: "Horizon Integration Test" }
});
assert.notEqual(refreshed.refreshToken, registration.refreshToken);
await request("/v1/auth/refresh", { method: "POST", expected: 401, body: { refreshToken: registration.refreshToken } });

const login = await request("/v1/auth/login", {
  method: "POST", body: { email, password: firstPassword, deviceName: "Horizon Integration Test" }
});
assert.equal(login.user.onboardingCompleted, true);

const forgot = await request("/v1/auth/forgot-password", { method: "POST", body: { email } });
assert.equal(typeof forgot.developmentResetToken, "string");
const unknownForgot = await request("/v1/auth/forgot-password", { method: "POST", body: { email: `unknown-${email}` } });
assert.equal(unknownForgot.accepted, true);
assert.equal(unknownForgot.developmentResetToken, undefined);
await request("/v1/auth/reset-password", { method: "POST", body: { token: forgot.developmentResetToken, password: secondPassword } });
await request("/v1/auth/reset-password", { method: "POST", expected: 400, body: { token: forgot.developmentResetToken, password: secondPassword } });
await request("/v1/auth/login", { method: "POST", expected: 401, body: { email, password: firstPassword } });

const newLogin = await request("/v1/auth/login", { method: "POST", body: { email, password: secondPassword } });
await request("/v1/auth/logout", { method: "POST", expected: 204, body: { refreshToken: newLogin.refreshToken } });
await request("/v1/auth/refresh", { method: "POST", expected: 401, body: { refreshToken: newLogin.refreshToken } });

console.info("Horizon authentication integration smoke test passed.");
} finally {
  if (createdUserId) await database.query("DELETE FROM users WHERE id=$1", [createdUserId]);
  await database.end();
}
