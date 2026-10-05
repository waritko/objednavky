import { cpSync, mkdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const destination = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.join(root, "artifacts/api");
mkdirSync(path.join(destination, "wwwroot"), { recursive: true });
cpSync(
  path.join(root, "frontend/dist"),
  path.join(destination, "wwwroot"),
  { recursive: true },
);
for (const launcher of ["run.cmd", "run.sh"]) {
  cpSync(path.join(root, "scripts", launcher), path.join(destination, launcher));
}
console.log(`Packaged frontend and launchers into ${destination}.`);
