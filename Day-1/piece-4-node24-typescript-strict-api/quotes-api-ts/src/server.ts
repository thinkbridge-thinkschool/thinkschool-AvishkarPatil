import http from "node:http";
import { initializeDatabase, closeDatabase } from "./database.ts";
import { QuoteRepository } from "./repository.ts";
import type { IQuoteRepository } from "./repository.ts";
import { handleRequest } from "./routes.ts";
import type { ProblemDetails } from "./types.ts";
import logger from "./logger.ts";

// --- Configuration ---
const PORT = Number(process.env["PORT"] ?? 3000);
const HOST = process.env["HOST"] ?? "0.0.0.0";

// --- DI container (manual, scoped per request) ---
// In the .NET version this is builder.Services.AddScoped<IQuoteRepository>.
// Here we create a fresh repository instance per request for the same scoped semantics.
function createScopedRepository(): IQuoteRepository {
  return new QuoteRepository();
}

// --- Exception middleware returning ProblemDetails ---
function exceptionMiddleware(
  err: unknown,
  res: http.ServerResponse
): void {
  if (err instanceof Error && err.name === "AbortError") {
    logger.warn("Request aborted by client");
    if (!res.headersSent) {
      res.writeHead(499).end(); // nginx-style "client closed request"
    }
    return;
  }

  logger.error({ err }, "Unhandled exception");

  if (res.headersSent) {
    res.destroy();
    return;
  }

  const problem: ProblemDetails = {
    type: "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    title: "Internal Server Error",
    status: 500,
    detail: "An unexpected error occurred. Please try again later.",
  };

  const body = JSON.stringify(problem);
  res.writeHead(500, {
    "Content-Type": "application/json",
    "Content-Length": Buffer.byteLength(body),
  });
  res.end(body);
}

// --- Server setup ---
// Apply migrations at startup (mirrors EF Core's Database.Migrate())
initializeDatabase();

const activeRequests = new Set<http.ServerResponse>();

const server = http.createServer(async (req, res) => {
  activeRequests.add(res);
  res.on("finish", () => activeRequests.delete(res));

  // Structured request logging
  const start = performance.now();
  res.on("finish", () => {
    const duration = (performance.now() - start).toFixed(2);
    logger.info(
      {
        method: req.method,
        url: req.url,
        status: res.statusCode,
        durationMs: duration,
      },
      "Request completed"
    );
  });

  try {
    // Scoped DI: fresh repository per request
    const repo = createScopedRepository();
    await handleRequest(repo, req, res);
  } catch (err: unknown) {
    exceptionMiddleware(err, res);
  }
});

server.listen(PORT, HOST, () => {
  logger.info({ port: PORT, host: HOST }, "Quotes API server started");
});

// --- Graceful shutdown ---
// Finishes in-flight requests, closes DB, exits clean.
let shuttingDown = false;

function gracefulShutdown(signal: string): void {
  if (shuttingDown) return;
  shuttingDown = true;

  logger.info({ signal }, "Shutdown signal received, draining connections...");

  // Stop accepting new connections
  server.close(() => {
    logger.info("Server closed, no new connections accepted");
    closeDatabase();
    logger.info("Graceful shutdown complete");
    process.exit(0);
  });

  // Give in-flight requests 10 seconds to finish
  const forceTimeout = setTimeout(() => {
    logger.warn("Force shutdown — killing remaining connections");
    for (const res of activeRequests) {
      res.destroy();
    }
    closeDatabase();
    process.exit(1);
  }, 10_000);

  // Don't let the timer keep the process alive if everything finishes sooner
  forceTimeout.unref();
}

process.on("SIGINT", () => gracefulShutdown("SIGINT"));
process.on("SIGTERM", () => gracefulShutdown("SIGTERM"));
