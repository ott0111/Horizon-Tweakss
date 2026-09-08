import "dotenv/config";
import { AuthService } from "./auth-service.js";
import { createApp } from "./app.js";
import { loadConfig } from "./config.js";
import { Database } from "./db.js";
import { Mailer } from "./mailer.js";
import { OAuthProviders } from "./providers.js";

const config = loadConfig();
const database = new Database(config);
const providers = new OAuthProviders(config);
const auth = new AuthService(database, config, new Mailer(config), providers);
const app = createApp(config, auth, providers);
const server = app.listen(config.port, config.host, () => console.info(`Horizon backend listening on ${config.appBaseUrl} (${config.host}:${config.port})`));

const shutdown = () => server.close(() => { void database.close().finally(() => process.exit(0)); });
process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
