import assert from "node:assert/strict";
import test from "node:test";
import { hashPassword, hashToken, validatePassword, verifyPassword } from "../src/crypto.js";
import { AppError } from "../src/errors.js";

test("password hashes use a random salt and verify safely", async () => {
  const first = await hashPassword("correct horse battery staple");
  const second = await hashPassword("correct horse battery staple");
  assert.notEqual(first, second);
  assert.equal(await verifyPassword("correct horse battery staple", first), true);
  assert.equal(await verifyPassword("wrong password", first), false);
});

test("opaque tokens are stored as deterministic SHA-256 hashes", () => {
  assert.equal(hashToken("secret"), "2bb80d537b1da3e38bd30361aa855686bde0eacd7162fef6a25fe97bf527a25b");
});

test("invalid passwords produce a controlled validation response", () => {
  assert.throws(() => validatePassword("too-short"), (error: unknown) =>
    error instanceof AppError && error.status === 400 && error.code === "VALIDATION_ERROR");
});
