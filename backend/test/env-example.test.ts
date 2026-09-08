import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const example = readFileSync(new URL("../.env.example", import.meta.url), "utf8");
const values = new Map(example.split(/\r?\n/)
  .filter(line => line && !line.startsWith("#") && line.includes("="))
  .map(line => {
    const separator = line.indexOf("=");
    return [line.slice(0, separator), line.slice(separator + 1)] as const;
  }));

test("environment example never contains provider or SMTP secrets", () => {
  for (const key of ["GOOGLE_CLIENT_SECRET", "DISCORD_CLIENT_SECRET", "EPIC_CLIENT_SECRET", "SMTP_PASSWORD"])
    assert.equal(values.get(key), "", `${key} must remain an empty placeholder`);
  assert.match(values.get("DATABASE_URL") ?? "", /replace_me/);
});

test("environment example documents every authentication credential", () => {
  for (const key of [
    "DATABASE_URL", "REQUIRE_EMAIL_VERIFICATION", "EMAIL_DELIVERY_MODE", "EMAIL_FROM",
    "SMTP_HOST", "SMTP_PORT", "SMTP_SECURE", "SMTP_USER", "SMTP_PASSWORD",
    "GOOGLE_CLIENT_ID", "GOOGLE_CLIENT_SECRET", "GOOGLE_REDIRECT_URI",
    "DISCORD_CLIENT_ID", "DISCORD_CLIENT_SECRET", "DISCORD_REDIRECT_URI",
    "EPIC_CLIENT_ID", "EPIC_CLIENT_SECRET", "EPIC_DEPLOYMENT_ID", "EPIC_REDIRECT_URI"
  ]) assert.equal(values.has(key), true, `${key} is missing from .env.example`);
});
