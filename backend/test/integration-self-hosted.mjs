import "dotenv/config";
import { AuthService } from "../dist/auth-service.js";
import { createApp } from "../dist/app.js";
import { loadConfig } from "../dist/config.js";
import { Database } from "../dist/db.js";
import { OAuthProviders } from "../dist/providers.js";

const config = loadConfig();
const database = new Database(config);
const providers = new OAuthProviders(config);
const quietDevelopmentMailer = { sendVerification: async () => {}, sendPasswordReset: async () => {} };
const auth = new AuthService(database, config, quietDevelopmentMailer, providers);
const server = createApp(config, auth, providers).listen(0, "127.0.0.1");

try {
  await new Promise((resolve, reject) => {
    server.once("listening", resolve);
    server.once("error", reject);
  });
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("The integration server did not bind to a TCP port.");
  process.env.HORIZON_INTEGRATION_BASE_URL = `http://127.0.0.1:${address.port}`;
  await import("./integration-smoke.mjs");
} finally {
  await new Promise(resolve => server.close(resolve));
  await database.close();
}
