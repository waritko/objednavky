import { cpSync, mkdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
mkdirSync(path.join(root, "artifacts/api/wwwroot"), { recursive: true });
cpSync(
  path.join(root, "frontend/dist"),
  path.join(root, "artifacts/api/wwwroot"),
  { recursive: true },
);
console.log("Packaged frontend into artifacts/api/wwwroot.");
