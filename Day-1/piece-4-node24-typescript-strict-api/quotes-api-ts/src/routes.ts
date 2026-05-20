import type { IncomingMessage, ServerResponse } from "node:http";
import type { IQuoteRepository } from "./repository.ts";
import type { CreateQuoteDto, ProblemDetails } from "./types.ts";
import { validateCreateQuote, validatePaginationParams } from "./validation.ts";
import logger from "./logger.ts";

function sendJson(res: ServerResponse, statusCode: number, data: unknown): void {
  const body = JSON.stringify(data);
  res.writeHead(statusCode, {
    "Content-Type": "application/json",
    "Content-Length": Buffer.byteLength(body),
  });
  res.end(body);
}

function sendProblem(res: ServerResponse, status: number, title: string, detail?: string): void {
  const problem: ProblemDetails = {
    type: `https://tools.ietf.org/html/rfc9110#section-15.5.${status - 399}`,
    title,
    status,
    detail,
  };
  sendJson(res, status, problem);
}

function parseBody(req: IncomingMessage, signal: AbortSignal): Promise<string> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];

    const onAbort = () => {
      req.destroy();
      const err = new Error("Request aborted");
      err.name = "AbortError";
      reject(err);
    };

    if (signal.aborted) {
      onAbort();
      return;
    }

    signal.addEventListener("abort", onAbort, { once: true });

    req.on("data", (chunk: Buffer) => {
      chunks.push(chunk);
      // Guard against oversized payloads (1 MB limit)
      const totalLength = chunks.reduce((sum, c) => sum + c.length, 0);
      if (totalLength > 1_048_576) {
        req.destroy();
        reject(new Error("Payload too large"));
      }
    });

    req.on("end", () => {
      signal.removeEventListener("abort", onAbort);
      resolve(Buffer.concat(chunks).toString("utf-8"));
    });

    req.on("error", (err) => {
      signal.removeEventListener("abort", onAbort);
      reject(err);
    });
  });
}

function parseIdFromPath(pathname: string): number | null {
  const match = /^\/api\/quotes\/(\d+)$/.exec(pathname);
  if (!match?.[1]) return null;
  const id = Number(match[1]);
  return Number.isInteger(id) && id > 0 ? id : null;
}

// Route handler: GET /api/quotes?page=N&size=N
function handleGetAll(
  repo: IQuoteRepository,
  url: URL,
  res: ServerResponse,
  signal: AbortSignal
): void {
  const { page, size, error } = validatePaginationParams(
    url.searchParams.get("page"),
    url.searchParams.get("size")
  );

  if (error) {
    sendJson(res, 400, error);
    return;
  }

  const result = repo.getAll(page, size, signal);
  sendJson(res, 200, result);
}

// Route handler: POST /api/quotes
async function handleCreate(
  repo: IQuoteRepository,
  req: IncomingMessage,
  res: ServerResponse,
  signal: AbortSignal
): Promise<void> {
  const rawBody = await parseBody(req, signal);

  let body: CreateQuoteDto;
  try {
    body = JSON.parse(rawBody) as CreateQuoteDto;
  } catch {
    sendProblem(res, 400, "Bad Request", "Request body must be valid JSON.");
    return;
  }

  const validationError = validateCreateQuote(body);
  if (validationError) {
    sendJson(res, 400, validationError);
    return;
  }

  const quote = repo.create((body.author as string).trim(), (body.text as string).trim(), signal);
  sendJson(res, 201, quote);
}

// Route handler: GET /api/quotes/:id
function handleGetById(
  repo: IQuoteRepository,
  id: number,
  res: ServerResponse,
  signal: AbortSignal
): void {
  const quote = repo.getById(id, signal);
  if (!quote) {
    sendProblem(res, 404, "Not Found", `Quote with id ${id} was not found.`);
    return;
  }
  sendJson(res, 200, quote);
}

// Route handler: DELETE /api/quotes/:id
function handleDelete(
  repo: IQuoteRepository,
  id: number,
  res: ServerResponse,
  signal: AbortSignal
): void {
  const deleted = repo.delete(id, signal);
  if (!deleted) {
    sendProblem(res, 404, "Not Found", `Quote with id ${id} was not found.`);
    return;
  }
  res.writeHead(204).end();
}

// Main router — maps incoming requests to the appropriate handler.
// This is the equivalent of MapQuoteEndpoints() in the .NET version.
export async function handleRequest(
  repo: IQuoteRepository,
  req: IncomingMessage,
  res: ServerResponse
): Promise<void> {
  const url = new URL(req.url ?? "/", `http://${req.headers.host ?? "localhost"}`);
  const pathname = url.pathname;
  const method = req.method ?? "GET";

  // Create an AbortController tied to the client disconnecting
  const controller = new AbortController();
  res.on("close", () => {
    if (!res.writableEnded) {
      controller.abort();
    }
  });
  const signal = controller.signal;

  // GET /api/quotes
  if (pathname === "/api/quotes" && method === "GET") {
    handleGetAll(repo, url, res, signal);
    return;
  }

  // POST /api/quotes
  if (pathname === "/api/quotes" && method === "POST") {
    await handleCreate(repo, req, res, signal);
    return;
  }

  // GET /api/quotes/:id
  const id = parseIdFromPath(pathname);
  if (id !== null && method === "GET") {
    handleGetById(repo, id, res, signal);
    return;
  }

  // DELETE /api/quotes/:id
  if (id !== null && method === "DELETE") {
    handleDelete(repo, id, res, signal);
    return;
  }

  sendProblem(res, 404, "Not Found", `No route matches ${method} ${pathname}`);
}
