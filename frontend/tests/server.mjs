import { spawn, spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";

const directory = mkdtempSync(path.join(tmpdir(), "restaurant-e2e-"));
const configuration = process.env.BUILD_CONFIGURATION || "Debug";
const api = path.resolve(
  process.env.E2E_PUBLISHED
    ? "../artifacts/api/RestaurantOrders.Api.dll"
    : `../backend/RestaurantOrders.Api/bin/${configuration}/net10.0/RestaurantOrders.Api.dll`,
);
const env = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: "Development",
  ASPNETCORE_URLS: "http://127.0.0.1:5080",
  Database__Provider: "Sqlite",
  ConnectionStrings__RestaurantOrders: `Data Source=${path.join(directory, "test.db")}`,
  Bootstrap__Username: "admin",
  Bootstrap__Password: "Browser-test-password-123",
  Logging__LogLevel__Default: "Warning",
  DataProtection__KeysPath: path.join(directory, "keys"),
};
for (const command of ["--migrate", "--bootstrap-admin"]) {
  const result = spawnSync("dotnet", [api, command], { env, stdio: "inherit" });
  if (result.status !== 0) {
    rmSync(directory, { recursive: true, force: true });
    process.exit(result.status || 1);
  }
}
const server = spawn("dotnet", [api], {
  env,
  stdio: "inherit",
  cwd: path.dirname(api),
});
const stop = () => server.kill();
process.on("SIGTERM", stop);
process.on("SIGINT", stop);
server.on("exit", (code) => {
  rmSync(directory, {
    recursive: true,
    force: true,
    maxRetries: 5,
    retryDelay: 200,
  });
  process.exit(code || 0);
});
