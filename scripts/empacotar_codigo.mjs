// Provides the corresponding application source without databases or secrets.
import { readdirSync, lstatSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = resolve(process.argv[2] || '.');
const output = resolve(process.argv[3] || 'artifacts/codigo-fonte.tar.gz');
const excluded = new Set(['node_modules', 'bin', 'obj', 'wwwroot', 'dist', '.angular', '__pycache__', 'test-results', 'playwright-report', 'maplibre']);
const files = [];
function walk(relative) {
  const absolute = resolve(root, relative);
  const stat = lstatSync(absolute);
  if (stat.isSymbolicLink()) throw new Error('Symlinks are not allowed in the source package.');
  if (stat.isDirectory()) {
    for (const entry of readdirSync(absolute).sort()) {
      if (!excluded.has(entry) && !entry.startsWith('.env') && !entry.endsWith('.log')) walk(`${relative}/${entry}`);
    }
  } else if (stat.isFile()) files.push(relative);
}
for (const path of ['README.md', 'LICENSE', 'NOTICE', '.gitignore', '.dockerignore', '.vercelignore', '.env.example', 'requirements.txt', 'requirements.lock', 'global.json', 'pytest.ini', 'Dockerfile.vercel', 'vercel.json', 'iniciar.sh', 'src', 'pipeline', 'scripts', 'server', 'app', 'docs', 'tests', 'deployment/snapshot.json']) walk(path);
mkdirSync(dirname(output), { recursive: true });
const result = spawnSync('tar', ['-czf', output, '--null', '-T', '-'], { cwd: root, input: files.join('\0') + '\0', encoding: 'utf8' });
if (result.status !== 0) throw new Error(result.stderr || 'Source packaging failed.');
console.log(`Código correspondente preparado: ${files.length} arquivos.`);
