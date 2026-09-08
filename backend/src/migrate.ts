import "dotenv/config";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { loadConfig } from "./config.js";
import { Database } from "./db.js";

const root = fileURLToPath(new URL("..", import.meta.url));
const database = new Database(loadConfig());
try {
  const sql = await readFile(resolve(root, "migrations", "001_auth_foundation.sql"), "utf8");
  await database.query(sql);
  console.info("Horizon authentication schema is current.");
} finally { await database.close(); }
