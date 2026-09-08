import pg from "pg";
import type { PoolClient, QueryResultRow } from "pg";
import type { AppConfig } from "./config.js";

export class Database {
  readonly pool: pg.Pool;
  constructor(config: AppConfig) {
    this.pool = new pg.Pool({ connectionString: config.databaseUrl, ssl: config.databaseSsl ? { rejectUnauthorized: true } : false, max: 10 });
  }
  query<T extends QueryResultRow>(text: string, values: unknown[] = []) { return this.pool.query<T>(text, values); }
  async transaction<T>(action: (client: PoolClient) => Promise<T>): Promise<T> {
    const client = await this.pool.connect();
    try {
      await client.query("BEGIN");
      const value = await action(client);
      await client.query("COMMIT");
      return value;
    } catch (error) {
      await client.query("ROLLBACK");
      throw error;
    } finally { client.release(); }
  }
  close() { return this.pool.end(); }
}
