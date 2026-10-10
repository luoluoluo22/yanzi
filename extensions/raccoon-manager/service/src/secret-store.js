import fs from "node:fs";
import { config } from "./config.js";

function normalizeRef(ref) {
  if (typeof ref !== "string" || !ref.startsWith("secret://")) throw new Error("Secret reference must use secret://...");
  const name = ref.slice("secret://".length);
  if (!name || name.includes("..")) throw new Error("Invalid secret reference.");
  return name;
}

function readFileSecrets() {
  try {
    const parsed = JSON.parse(fs.readFileSync(config.secretFile, "utf8"));
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) throw new Error("Secret file must contain a JSON object.");
    return parsed;
  } catch (error) {
    if (error.code === "ENOENT") return {};
    throw error;
  }
}

export function resolveSecretRef(ref) {
  const name = normalizeRef(ref);
  if (name.startsWith("env/")) {
    const envName = name.slice(4);
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(envName)) throw new Error("Invalid environment secret name.");
    const value = process.env[envName];
    if (value == null) throw new Error(`Secret environment variable is not configured: ${envName}`);
    return value;
  }
  const secrets = readFileSecrets();
  const value = secrets[name];
  if (typeof value !== "string") throw new Error(`Secret is not configured: ${name}`);
  return value;
}

export function listSecretRefs() {
  return Object.keys(readFileSecrets()).sort().map(name => `secret://${name}`);
}
