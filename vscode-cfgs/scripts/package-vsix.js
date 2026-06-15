const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const root = path.resolve(__dirname, '..');
const repoRoot = path.resolve(root, '..');
const serverOutput = path.join(repoRoot, 'dist', 'Debug', 'net10.0');
const outFile = path.join(root, 'cfgs-vscode-0.5.0.vsix');
const files = [
  'package.json',
  'README.md',
  'extension.js',
  'language-configuration.json',
  'syntaxes/cfgs.tmGrammar.json'
];

function collectServerFiles() {
  if (!fs.existsSync(serverOutput)) {
    return [];
  }

  const serverFiles = new Set([
    'CFGS.Lsp.dll',
    'CFGS.Lsp.deps.json',
    'CFGS.Lsp.runtimeconfig.json',
    'CFGS_VM.dll'
  ]);

  return fs.readdirSync(serverOutput)
    .filter((file) => serverFiles.has(file))
    .map((file) => ({
      name: `extension/server/${file}`,
      content: fs.readFileSync(path.join(serverOutput, file))
    }));
}

function crc32(buffer) {
  let crc = ~0;
  for (const byte of buffer) {
    crc ^= byte;
    for (let i = 0; i < 8; i++) {
      crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
    }
  }
  return (~crc) >>> 0;
}

function dosTime(date) {
  return ((date.getHours() & 0x1f) << 11) | ((date.getMinutes() & 0x3f) << 5) | ((date.getSeconds() / 2) & 0x1f);
}

function dosDate(date) {
  return (((date.getFullYear() - 1980) & 0x7f) << 9) | (((date.getMonth() + 1) & 0xf) << 5) | (date.getDate() & 0x1f);
}

function writeUInt32(buffer, value, offset) {
  buffer.writeUInt32LE(value >>> 0, offset);
}

function createEntry(name, content, offset) {
  const compressed = zlib.deflateRawSync(content);
  const crc = crc32(content);
  const now = new Date();
  const nameBytes = Buffer.from(name, 'utf8');
  const local = Buffer.alloc(30 + nameBytes.length);
  writeUInt32(local, 0x04034b50, 0);
  local.writeUInt16LE(20, 4);
  local.writeUInt16LE(0x0800, 6);
  local.writeUInt16LE(8, 8);
  local.writeUInt16LE(dosTime(now), 10);
  local.writeUInt16LE(dosDate(now), 12);
  writeUInt32(local, crc, 14);
  writeUInt32(local, compressed.length, 18);
  writeUInt32(local, content.length, 22);
  local.writeUInt16LE(nameBytes.length, 26);
  nameBytes.copy(local, 30);

  const central = Buffer.alloc(46 + nameBytes.length);
  writeUInt32(central, 0x02014b50, 0);
  central.writeUInt16LE(20, 4);
  central.writeUInt16LE(20, 6);
  central.writeUInt16LE(0x0800, 8);
  central.writeUInt16LE(8, 10);
  central.writeUInt16LE(dosTime(now), 12);
  central.writeUInt16LE(dosDate(now), 14);
  writeUInt32(central, crc, 16);
  writeUInt32(central, compressed.length, 20);
  writeUInt32(central, content.length, 24);
  central.writeUInt16LE(nameBytes.length, 28);
  writeUInt32(central, offset, 42);
  nameBytes.copy(central, 46);

  return { local: Buffer.concat([local, compressed]), central };
}

const manifest = Buffer.from(`<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Language="en-US" Id="cfgs-vscode" Version="0.5.0" Publisher="blackfox1991" />
    <DisplayName>CFGS Language Tools</DisplayName>
    <Description xml:space="preserve">Visual Studio Code support for ConfigurationScript.</Description>
    <Tags>CFGS,ConfigurationScript,LSP,linter,formatter</Tags>
    <Categories>Programming Languages</Categories>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Code" Version="[1.90.0,)" />
  </Installation>
  <Dependencies />
  <Assets>
    <Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" />
  </Assets>
</PackageManifest>`, 'utf8');

const contentTypes = Buffer.from(`<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="json" ContentType="application/json" />
  <Default Extension="js" ContentType="application/javascript" />
  <Default Extension="md" ContentType="text/markdown" />
  <Default Extension="dll" ContentType="application/octet-stream" />
  <Default Extension="vsixmanifest" ContentType="text/xml" />
  <Default Extension="xml" ContentType="text/xml" />
</Types>`, 'utf8');

const allFiles = [
  { name: '[Content_Types].xml', content: contentTypes },
  { name: 'extension.vsixmanifest', content: manifest },
  ...files.map((file) => ({
    name: `extension/${file.replace(/\\/g, '/')}`,
    content: fs.readFileSync(path.join(root, file))
  })),
  ...collectServerFiles()
];

let offset = 0;
const locals = [];
const centrals = [];
for (const file of allFiles) {
  const entry = createEntry(file.name, file.content, offset);
  locals.push(entry.local);
  centrals.push(entry.central);
  offset += entry.local.length;
}

const centralDir = Buffer.concat(centrals);
const end = Buffer.alloc(22);
writeUInt32(end, 0x06054b50, 0);
end.writeUInt16LE(allFiles.length, 8);
end.writeUInt16LE(allFiles.length, 10);
writeUInt32(end, centralDir.length, 12);
writeUInt32(end, offset, 16);

fs.writeFileSync(outFile, Buffer.concat([...locals, centralDir, end]));
console.log(outFile);
