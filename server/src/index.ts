// index.ts - Express + ws startup
import express from "express";
import cors from "cors";
import http from "node:http";
import agentsRouter from "./routes/agents.js";
import { initWs } from "./ws.js";
import "./db.js"; // side effect: init SQLite + seed

const PORT = process.env.PORT ? parseInt(process.env.PORT) : 3001;

const app = express();
app.use(cors());
app.use(express.json());

// health check
app.get("/health", (_req, res) => {
  res.json({ ok: true, ts: new Date().toISOString() });
});

// REST routes
app.use("/api/agents", agentsRouter);

// HTTP server (ws needs this instead of express directly)
const server = http.createServer(app);

// WebSocket
initWs(server);

server.listen(PORT, () => {
  console.log(`[server] http://localhost:${PORT}`);
  console.log(`[server] ws://localhost:${PORT}/events`);
});
