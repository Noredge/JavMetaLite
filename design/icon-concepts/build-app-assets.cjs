const fs = require("fs");
const path = require("path");

const root = __dirname;
const renders = path.join(root, "renders");
const appBrand = path.resolve(root, "..", "..", "JavMetaLite.App", "Resources", "Brand");
const icoSizes = [16, 24, 32, 48, 64, 128, 256];

function readPng(size) {
  return fs.readFileSync(path.join(renders, `javmetalite-icon-e-${size}.png`));
}

function createIco(images) {
  const headerSize = 6;
  const entrySize = 16;
  const directorySize = headerSize + entrySize * images.length;
  const header = Buffer.alloc(directorySize);

  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);

  let imageOffset = directorySize;
  images.forEach(({ size, data }, index) => {
    const entryOffset = headerSize + entrySize * index;
    header.writeUInt8(size === 256 ? 0 : size, entryOffset);
    header.writeUInt8(size === 256 ? 0 : size, entryOffset + 1);
    header.writeUInt8(0, entryOffset + 2);
    header.writeUInt8(0, entryOffset + 3);
    header.writeUInt16LE(1, entryOffset + 4);
    header.writeUInt16LE(32, entryOffset + 6);
    header.writeUInt32LE(data.length, entryOffset + 8);
    header.writeUInt32LE(imageOffset, entryOffset + 12);
    imageOffset += data.length;
  });

  return Buffer.concat([header, ...images.map(({ data }) => data)]);
}

fs.mkdirSync(appBrand, { recursive: true });
const images = icoSizes.map((size) => ({ size, data: readPng(size) }));
fs.writeFileSync(path.join(appBrand, "JavMetaLite.ico"), createIco(images));
fs.copyFileSync(
  path.join(renders, "javmetalite-icon-e-64.png"),
  path.join(appBrand, "JavMetaLite-64.png"),
);

console.log(`Built JavMetaLite.ico with ${icoSizes.join(", ")} px images.`);
