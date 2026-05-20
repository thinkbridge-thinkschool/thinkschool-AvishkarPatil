import type Database from "better-sqlite3";
import type { Quote, PagedResult } from "./types.ts";
import { getDatabase } from "./database.ts";
import logger from "./logger.ts";

export interface IQuoteRepository {
  getAll(page: number, size: number, signal?: AbortSignal): PagedResult<Quote>;
  getById(id: number, signal?: AbortSignal): Quote | undefined;
  create(author: string, text: string, signal?: AbortSignal): Quote;
  delete(id: number, signal?: AbortSignal): boolean;
}

// Checks if the request was aborted before executing a DB operation.
// better-sqlite3 is synchronous, so we check before each call rather than
// mid-query. This mirrors the CancellationToken pattern from the .NET version.
function checkAborted(signal?: AbortSignal): void {
  if (signal?.aborted) {
    const err = new Error("Request aborted");
    err.name = "AbortError";
    throw err;
  }
}

export class QuoteRepository implements IQuoteRepository {
  private readonly db: Database.Database;

  constructor() {
    this.db = getDatabase();
  }

  getAll(page: number, size: number, signal?: AbortSignal): PagedResult<Quote> {
    checkAborted(signal);

    const countRow = this.db.prepare("SELECT COUNT(*) as count FROM quotes").get() as
      | { count: number }
      | undefined;
    const totalCount = countRow?.count ?? 0;

    checkAborted(signal);

    const offset = (page - 1) * size;
    const items = this.db
      .prepare("SELECT id, author, text, created_at FROM quotes ORDER BY id DESC LIMIT ? OFFSET ?")
      .all(size, offset) as Quote[];

    logger.debug({ page, size, totalCount }, "Fetched quotes page");

    return {
      items,
      page,
      size,
      totalCount,
      totalPages: Math.ceil(totalCount / size),
    };
  }

  getById(id: number, signal?: AbortSignal): Quote | undefined {
    checkAborted(signal);

    const row = this.db
      .prepare("SELECT id, author, text, created_at FROM quotes WHERE id = ?")
      .get(id) as Quote | undefined;

    logger.debug({ id, found: !!row }, "Fetched quote by id");
    return row;
  }

  create(author: string, text: string, signal?: AbortSignal): Quote {
    checkAborted(signal);

    const result = this.db
      .prepare("INSERT INTO quotes (author, text) VALUES (?, ?)")
      .run(author, text);

    const quote = this.getById(Number(result.lastInsertRowid), signal);
    if (!quote) {
      throw new Error("Failed to retrieve created quote");
    }

    logger.info({ id: quote.id, author }, "Quote created");
    return quote;
  }

  delete(id: number, signal?: AbortSignal): boolean {
    checkAborted(signal);

    const result = this.db.prepare("DELETE FROM quotes WHERE id = ?").run(id);
    const deleted = result.changes > 0;

    logger.info({ id, deleted }, "Quote delete attempted");
    return deleted;
  }
}
