import { spawn, spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const configuration = process.env.BUILD_CONFIGURATION || 'Debug';
const image = readFileSync(path.join(root, 'scripts/sqlserver-image.txt'), 'utf8').trim();
const password = `Test-${randomBytes(24).toString('hex')}!`;
const name = `restaurant-orders-test-${randomBytes(8).toString('hex')}`;
let started = false;
function docker(args, options = {}) {
  const result = spawnSync('docker', args, { encoding: 'utf8', ...options });
  if (result.status !== 0) throw new Error(result.stderr || 'Docker command failed.');
  return result.stdout?.trim();
}
function cleanup() {
  if (started) { spawnSync('docker', ['rm', '--force', name], { stdio: 'ignore' }); started = false; }
}
process.on('SIGINT', () => { cleanup(); process.exit(130); });
process.on('SIGTERM', () => { cleanup(); process.exit(143); });
try {
  docker(['run', '--detach', '--name', name, '--publish', '127.0.0.1::1433',
    '--env', 'ACCEPT_EULA=Y', '--env', 'MSSQL_PID=Developer', '--env', `MSSQL_SA_PASSWORD=${password}`, image]);
  started = true;
  const port = docker(['inspect', '--format', '{{(index (index .NetworkSettings.Ports "1433/tcp") 0).HostPort}}', name]);
  let ready = false;
  for (let attempt = 0; attempt < 60; attempt++) {
    const result = spawnSync('docker', ['exec', name, '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-P', password, '-C', '-Q', 'SELECT 1', '-b'], { stdio: 'ignore' });
    if (result.status === 0) { ready = true; break; }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  if (!ready) throw new Error('Isolated SQL Server did not become ready within 60 seconds.');
  const env = { ...process.env, TEST_SQLSERVER_CONNECTION: `Server=127.0.0.1,${port};User Id=sa;Password=${password};TrustServerCertificate=True;Connect Timeout=10` };
  const child = spawn('dotnet', ['run', '--project', 'backend/RestaurantOrders.SmokeTests', '--configuration', configuration, '--no-build', '--no-restore', '--', '--sqlserver'], { cwd: root, env, stdio: 'inherit' });
  process.exitCode = await new Promise((resolve, reject) => { child.on('error', reject); child.on('exit', code => resolve(code || 0)); });
} finally { cleanup(); }
