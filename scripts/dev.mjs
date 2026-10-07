import { spawn } from 'node:child_process';
import { access, cp, readFile } from 'node:fs/promises';
import { parseEnv } from 'node:util';
import { createServer } from 'node:net';
import { fileURLToPath } from 'node:url';
import { join, resolve } from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
const args = process.argv.slice(2);
if (args.some(a => !['--nativo', '--producao'].includes(a))) {
  process.stderr.write('Uso: ./iniciar.sh [--nativo] [--producao]\n'); process.exit(2);
}
const native = args.includes('--nativo'), production = args.includes('--producao');
let fileEnvironment = {};
try { fileEnvironment = parseEnv(await readFile(join(root, '.env'), 'utf8')); } catch (e) { if (e.code !== 'ENOENT') throw e; }
const environment = { ...fileEnvironment, ...process.env };
const data = resolve(root, environment.CEARA_DATA_DIR || 'data');
const dotnet = environment.CEARA_DOTNET || 'dotnet';
const children = new Set();
const groups = new Set();
const container = `ceara-${process.pid}`;
let stopping = false, containerStarted = false;
function run(command, arguments_, options = {}) {
  const child = spawn(command, arguments_, { cwd: root, stdio: 'inherit', env: environment, detached: process.platform !== 'win32', ...options });
  if (child.pid) groups.add(child.pid);
  children.add(child); child.once('exit', () => { children.delete(child); if (!stopping) groups.delete(child.pid); });
  return child;
}
async function completed(command, arguments_, options) {
  const child = run(command, arguments_, options);
  await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', code => code === 0 ? resolve() : reject(new Error(`${command} encerrou com código ${code}.`))); });
}
async function stop(code = 0) {
  if (stopping) return; stopping = true;
  if (containerStarted) await new Promise(resolve => { const child = spawn('docker', ['stop', '-t', '5', container], { stdio: 'ignore', env: environment }); child.once('exit', resolve); child.once('error', resolve); });
  const terminate = signal => {
    if (process.platform === 'win32') { for (const child of children) child.kill(signal); return; }
    for (const pid of groups) { try { process.kill(-pid, signal); } catch (error) { if (error.code !== 'ESRCH') throw error; } }
  };
  terminate('SIGTERM');
  await new Promise(resolve => setTimeout(resolve, 1500));
  terminate('SIGKILL');
  process.exitCode = code;
}
process.on('SIGINT', () => void stop()); process.on('SIGTERM', () => void stop());
try {
  for (const port of production ? [3000] : [3000, 8000]) {
    const server = createServer();
    await new Promise((resolve, reject) => { server.once('error', () => reject(new Error(`A porta ${port} está em uso. Encerre o servidor anterior.`))); server.listen(port, '127.0.0.1', resolve); });
    await new Promise(resolve => server.close(resolve));
  }
  try { await access(join(root, 'app/node_modules/@angular/cli')); }
  catch { await completed('npm', ['ci'], { cwd: join(root, 'app') }); }
  if (native && production) {
    await completed('npm', ['run', 'build'], { cwd: join(root, 'app') });
    const output = join(root, 'artifacts', 'local-production');
    await completed(dotnet, ['publish', 'server/Ceara.Api', '-c', 'Release', '-o', output, '/p:UseAppHost=false']);
    await cp(join(root, 'app/dist/ceara/browser'), join(output, 'wwwroot'), { recursive: true });
    await completed('node', ['scripts/fetch-snapshot.mjs', 'deployment/snapshot.json', join(output, 'snapshot'), 'deployment/snapshot-input/snapshot.tar.br']);
    run(dotnet, ['Ceara.Api.dll'], { cwd: output, env: { ...environment, PORT: '3000', CEARA_CONTAINER: '1', ASPNETCORE_ENVIRONMENT: 'Production',
      CEARA_PUBLIC: '1', CEARA_DATA_PROFILE: 'minimizado', CEARA_DATA_DIR: join(output, `data-${process.pid}`), CEARA_SNAPSHOT_ARCHIVE: join(output, 'snapshot/snapshot.tar.br'), CEARA_SNAPSHOT_MANIFEST: join(output, 'snapshot/snapshot.json') } });
  } else if (native) {
    run(dotnet, ['watch', '--no-hot-reload', '--project', 'server/Ceara.Api', 'run'], { env: { ...environment, CEARA_PROJECT_ROOT: root, CEARA_DATA_DIR: data, PORT: '8000', ASPNETCORE_ENVIRONMENT: 'Development' } });
  } else {
    await completed('docker', ['info', '--format', '{{.ServerVersion}}']);
    await completed('docker', ['build', '-f', 'Dockerfile.vercel', '--target', production ? 'production' : 'development', '-t', production ? 'ceara:local' : 'ceara:dev', '.']);
    const dockerArgs = ['run', '--rm', '--name', container];
    // Docker inherits the parsed values without putting credentials in arguments.
    // Host data paths belong only to the development mount, not the release image.
    for (const name of ['GEMINI_API_KEY', 'GEMINI_MODEL']) {
      if (environment[name] !== undefined) dockerArgs.push('-e', name);
    }
    if (production) dockerArgs.push('-p', '127.0.0.1:3000:8080', 'ceara:local');
    else dockerArgs.push('-p', '127.0.0.1:8000:8000', '-v', `${root}:/workspace`, '-v', `${data}:/data`, '--user', `${process.getuid()}:${process.getgid()}`,
      '-e', 'CEARA_DATA_DIR=/data', '-e', 'DOTNET_CLI_HOME=/tmp', '-e', 'NUGET_PACKAGES=/tmp/nuget', '-e', 'DOTNET_USE_POLLING_FILE_WATCHER=1', 'ceara:dev');
    containerStarted = true; run('docker', dockerArgs);
  }
  if (!production) run('npm', ['run', 'dev'], { cwd: join(root, 'app'), env: process.env });
  process.stdout.write('\nMapa de Oportunidades Ceará: http://localhost:3000\nCtrl+C encerra os serviços.\n');
  for (const child of children) {
    child.once('error', error => { process.stderr.write(`${error.message}\n`); void stop(1); });
    child.once('exit', code => { if (!stopping) void stop(code || 0); });
  }
} catch (error) { process.stderr.write(`${error.message}\n`); await stop(1); }
