import { createHash } from 'node:crypto';
import { createReadStream, createWriteStream } from 'node:fs';
import { copyFile, mkdir, readFile, rm } from 'node:fs/promises';
import { pipeline } from 'node:stream/promises';
import { join } from 'node:path';

const [manifestPath, output, localArchive] = process.argv.slice(2);
if (!manifestPath || !output) throw new Error('Uso: node scripts/fetch-snapshot.mjs manifesto destino [arquivo-local]');
const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
const required = ['uf/CE/contatos.db', 'ibge/malha_23.geojson', 'ibge/malha_br.geojson'];
if (!manifest.version || !/^[a-f0-9]{64}$/.test(manifest.archive_sha256)
    || Object.keys(manifest.files ?? {}).length < 3 || Object.keys(manifest.files ?? {}).length > 11 || Object.keys(manifest.files ?? {}).some(name => !required.includes(name) && !/^uf\/CE\/downloads\/[a-f0-9]{24}\/(catalogo\.json|contatos\.db|contatos\.csv\.gz|municipios\.csv\.gz|segmentos\.csv\.gz|metadados\.json|malha_23\.geojson|malha_br\.geojson)$/.test(name)) || required.some(name => !/^[a-f0-9]{64}$/.test(manifest.files[name])))
  throw new Error('Manifesto de snapshot inválido. Prepare um release validado.');
await mkdir(output, { recursive: true });
const archive = join(output, 'snapshot.tar.br');
try {
  if (manifest.url) {
    const url = new URL(manifest.url);
    if (url.protocol !== 'https:' || url.username || url.password) throw new Error('O snapshot remoto requer uma URL HTTPS pública.');
    const response = await fetch(url, { signal: AbortSignal.timeout(300000) });
    if (!response.ok || !response.body) throw new Error(`Não foi possível baixar o snapshot (${response.status}).`);
    await pipeline(response.body, createWriteStream(archive));
  } else {
    if (!localArchive) throw new Error('Snapshot local ausente. Execute preparar_release.py --fixar.');
    try { await copyFile(localArchive, archive); }
    catch { throw new Error('Snapshot local ausente. Execute preparar_release.py --fixar antes do build ou deploy via CLI.'); }
  }
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(archive)) hash.update(chunk);
  if (hash.digest('hex') !== manifest.archive_sha256) throw new Error('Checksum do snapshot inválido. O build foi interrompido.');
  await copyFile(manifestPath, join(output, 'snapshot.json'));
  await rm(join(output, 'snapshot.tar.gz'), { force: true });
  process.stdout.write(`Snapshot verificado: ${manifest.version}\n`);
} catch (error) { await rm(archive, { force: true }); throw error; }
