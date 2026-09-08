import { randomBytes, scrypt as scryptCallback, timingSafeEqual, createHash } from "node:crypto";
import { badRequest } from "./errors.js";
const N = 16384;
const R = 8;
const P = 1;
const KEY_LENGTH = 64;

const scrypt = (password: string, salt: Buffer, length: number, options: { N: number; r: number; p: number; maxmem: number }) =>
  new Promise<Buffer>((resolve, reject) => scryptCallback(password, salt, length, options,
    (error, key) => error ? reject(error) : resolve(key)));

export const randomToken = (bytes = 32) => randomBytes(bytes).toString("base64url");
export const hashToken = (token: string) => createHash("sha256").update(token, "utf8").digest("hex");

export async function hashPassword(password: string): Promise<string> {
  const salt = randomBytes(16);
  const derived = await scrypt(password, salt, KEY_LENGTH, { N, r: R, p: P, maxmem: 64 * 1024 * 1024 });
  return `scrypt$${N}$${R}$${P}$${salt.toString("base64url")}$${derived.toString("base64url")}`;
}

export async function verifyPassword(password: string, stored: string): Promise<boolean> {
  const [algorithm, n, r, p, saltValue, hashValue] = stored.split("$");
  if (algorithm !== "scrypt" || !n || !r || !p || !saltValue || !hashValue) return false;
  const expected = Buffer.from(hashValue, "base64url");
  const actual = await scrypt(password, Buffer.from(saltValue, "base64url"), expected.length, {
    N: Number(n), r: Number(r), p: Number(p), maxmem: 64 * 1024 * 1024
  });
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

export function validatePassword(password: string): void {
  if (password.length < 10) throw badRequest("VALIDATION_ERROR", "Use at least 10 characters for your password.");
  if (password.length > 256) throw badRequest("VALIDATION_ERROR", "Password is too long.");
}
