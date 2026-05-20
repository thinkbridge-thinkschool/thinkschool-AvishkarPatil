import Database from "better-sqlite3";
import path from "node:path";
import logger from "./logger.ts";

const DB_PATH = process.env["DB_PATH"] ?? path.join(process.cwd(), "quotes.db");

let db: Database.Database | null = null;

export function getDatabase(): Database.Database {
  if (!db) {
    db = new Database(DB_PATH);
    db.pragma("journal_mode = WAL");
    db.pragma("foreign_keys = ON");
    logger.info({ dbPath: DB_PATH }, "Database connection opened");
  }
  return db;
}

export function initializeDatabase(): void {
  const database = getDatabase();

  database.exec(`
    CREATE TABLE IF NOT EXISTS quotes (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      author TEXT NOT NULL,
      text TEXT NOT NULL,
      created_at TEXT NOT NULL DEFAULT (datetime('now'))
    )
  `);

  logger.info("Database schema initialized (migration applied)");
}

export function closeDatabase(): void {
  if (db) {
    db.close();
    db = null;
    logger.info("Database connection closed");
  }
}
