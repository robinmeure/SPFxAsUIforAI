// Builds teams/TeamsSPFxApp.zip from teams/manifest.json and its icons. SharePoint's "Sync to Teams" deploys
// this package instead of generating one, which lets the personal app have both a Chat and a Settings tab.
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { crc32 } from 'node:zlib';

const teamsDir = join(import.meta.dirname, '..', 'teams');
const manifest = JSON.parse(readFileSync(join(teamsDir, 'manifest.json'), 'utf8'));
const files = ['manifest.json', manifest.icons.color, manifest.icons.outline];

// Minimal ZIP writer with stored (uncompressed) entries at the archive root.
const locals = [];
const centrals = [];
let offset = 0;
for (const name of files) {
  const data = readFileSync(join(teamsDir, name));
  const nameBytes = Buffer.from(name, 'utf8');
  const crc = crc32(data);
  const header = (signature, size) => {
    const b = Buffer.alloc(size);
    b.writeUInt32LE(signature, 0);
    return b;
  };

  const local = header(0x04034b50, 30);
  local.writeUInt16LE(20, 4);
  local.writeUInt16LE(0x0800, 6); // UTF-8 names
  local.writeUInt32LE(crc, 14);
  local.writeUInt32LE(data.length, 18);
  local.writeUInt32LE(data.length, 22);
  local.writeUInt16LE(nameBytes.length, 26);
  locals.push(local, nameBytes, data);

  const central = header(0x02014b50, 46);
  central.writeUInt16LE(20, 4);
  central.writeUInt16LE(20, 6);
  central.writeUInt16LE(0x0800, 8);
  central.writeUInt32LE(crc, 16);
  central.writeUInt32LE(data.length, 20);
  central.writeUInt32LE(data.length, 24);
  central.writeUInt16LE(nameBytes.length, 28);
  central.writeUInt32LE(offset, 42);
  centrals.push(central, nameBytes);

  offset += local.length + nameBytes.length + data.length;
}

const centralSize = centrals.reduce((n, b) => n + b.length, 0);
const end = Buffer.alloc(22);
end.writeUInt32LE(0x06054b50, 0);
end.writeUInt16LE(files.length, 8);
end.writeUInt16LE(files.length, 10);
end.writeUInt32LE(centralSize, 12);
end.writeUInt32LE(offset, 16);

const output = join(teamsDir, 'TeamsSPFxApp.zip');
writeFileSync(output, Buffer.concat([...locals, ...centrals, end]));
console.log(`Created ${output} (${files.join(', ')})`);
