import { readFileSync } from "node:fs";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerTools } from "./tools.js";
import { version } from "./version.js";

const icon = readFileSync(new URL("../assets/raccoon-128.png", import.meta.url)).toString("base64");

export function createRaccoonServer() {
  const server = new McpServer({
    name: "raccoon-mcp",
    title: "Raccoon",
    version,
    description: "Local development runtime for AI-assisted coding, devices, visual feedback, data, builds and processes.",
    icons: [{
      src: `data:image/png;base64,${icon}`,
      mimeType: "image/png",
      sizes: ["128x128"]
    }]
  });

  registerTools(server);
  return server;
}
