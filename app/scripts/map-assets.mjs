import { copyFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// MapLibre 6 usa workers ESM. Servi-los como arquivos locais explícitos
// evita que o bundler reescreva import.meta.url para uma URL de chunk.
const distribution = dirname(fileURLToPath(import.meta.resolve('maplibre-gl')));
const destination = fileURLToPath(new URL('../public/maplibre/', import.meta.url));
await mkdir(destination, { recursive: true });
for (const name of ['maplibre-gl-worker.mjs', 'maplibre-gl-shared.mjs', 'maplibre-gl-worker.mjs.map', 'maplibre-gl-shared.mjs.map']) {
  await copyFile(join(distribution, name), join(destination, name));
}
