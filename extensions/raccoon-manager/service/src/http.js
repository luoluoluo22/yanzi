import express from "express";
import { timingSafeEqual } from "node:crypto";
import { hostHeaderValidation } from "@modelcontextprotocol/sdk/server/middleware/hostHeaderValidation.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { config, assertHttpSafety } from "./config.js";
import { createRaccoonServer } from "./server.js";
import { version } from "./version.js";
import { installOAuth } from "./oauth.js";
import fs from "node:fs";
import { deploymentState, releaseDeploymentState } from "./deployment-state.js";

export function createHttpApp() {
  assertHttpSafety();
  const app = express();
  let activeRequests = 0;
  app.use((req,res,next) => {
    if (!req.path.startsWith("/__raccoon/") && req.path !== "/health") {
      activeRequests++;
      let done = false;
      const finish = () => { if (!done) { done = true; activeRequests--; } };
      res.once("finish",finish); res.once("close",finish);
    }
    next();
  });
  app.disable("x-powered-by");
  const hosts = config.allowedHosts.length ? config.allowedHosts : [config.host, "localhost", "127.0.0.1", "[::1]"];
  app.use(hostHeaderValidation(hosts));
  app.use((_req, res, next) => {
    res.setHeader("Cache-Control", "no-store");
    res.setHeader("X-Content-Type-Options", "nosniff");
    next();
  });
  app.get("/health", (_req, res) => res.json({ ok: true, name: "raccoon-mcp", version, transport: "http" }));
  const oauthAuth = config.publicUrl ? installOAuth(app, {
    issuer: config.publicUrl, password: config.oauthPassword, stateFile: config.oauthStateFile
  }) : null;
  app.use("/__raccoon", (req,res,next) => {
    const received=Buffer.from(req.headers.authorization || "");
    const expected=Buffer.from(`Bearer ${config.token}`);
    if (!config.token || !["127.0.0.1","localhost","[::1]"].includes(req.hostname) || received.length !== expected.length || !timingSafeEqual(received,expected)) return res.sendStatus(404);
    next();
  });
  app.get("/__raccoon/state", (_req,res) => res.json(deploymentState(app.locals.oauthProvider,activeRequests)));
  app.post("/__raccoon/release", async (_req,res) => {
    const state=deploymentState(app.locals.oauthProvider,activeRequests);
    if(state.busy) return res.status(409).json({error:"Deployment deferred",state});
    await releaseDeploymentState(); res.json({ok:true});
  });
  app.post("/__raccoon/activate", (_req,res) => {
    const provider=app.locals.oauthProvider;
    if(provider) provider.state=JSON.parse(fs.readFileSync(config.oauthStateFile,"utf8"));
    res.json({ok:true});
  });
  app.get("/assets/raccoon-128.png", (_req,res) => res.type("png").send(fs.readFileSync(new URL("../assets/raccoon-128.png",import.meta.url))));
  app.get("/assets/raccoon-256.png", (_req,res) => res.type("png").send(fs.readFileSync(new URL("../assets/raccoon-256.png",import.meta.url))));
  app.use("/mcp", (req, res, next) => {
    if (req.headers.origin && !config.allowedOrigins.includes(req.headers.origin)) {
      return res.status(403).json({ error: "Origin not allowed" });
    }
    // Public tunnel traffic must use OAuth; the static local token is never accepted there.
    if (oauthAuth && req.hostname === new URL(config.publicUrl).hostname) return oauthAuth(req, res, next);
    if (config.token) {
      const expected = Buffer.from(`Bearer ${config.token}`);
      const received = Buffer.from(req.headers.authorization || "");
      if (expected.length !== received.length || !timingSafeEqual(expected, received)) {
        res.setHeader("WWW-Authenticate", 'Bearer realm="raccoon-mcp"');
        return res.status(401).json({ error: "Unauthorized" });
      }
    }
    next();
  });
  app.use("/mcp", express.json({ limit: config.maxMessageBytes }));
  app.post("/mcp", async (req, res) => {
    const server = createRaccoonServer();
    const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
    let closed = false;
    const cleanup = async () => {
      if (closed) return;
      closed = true;
      await transport.close().catch(() => {});
      await server.close().catch(() => {});
    };
    res.once("close", cleanup);
    try {
      await server.connect(transport);
      await transport.handleRequest(req, res, req.body);
    } catch (error) {
      console.error("Raccoon HTTP request failed:", error.message);
      if (!res.headersSent) res.status(500).json({ jsonrpc: "2.0", error: { code: -32603, message: "Internal server error" }, id: null });
      await cleanup();
    }
  });
  app.all("/mcp", (_req, res) => {
    res.setHeader("Allow", "POST");
    res.status(405).json({ jsonrpc: "2.0", error: { code: -32000, message: "Method not allowed in stateless mode." }, id: null });
  });
  app.use((error, _req, res, _next) => {
    res.status(error.type === "entity.too.large" ? 413 : error instanceof SyntaxError ? 400 : 500)
      .json({ error: error.type === "entity.too.large" ? "Request body too large" : "Invalid request" });
  });
  return app;
}
