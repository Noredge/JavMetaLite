const fs = require("fs");
const path = require("path");
const sharp = require("sharp");

const root = __dirname;
const source = fs.readFileSync(path.join(root, "javmetalite-icon-e.svg"));
const smallSource = fs.readFileSync(path.join(root, "javmetalite-icon-e-small.svg"));
const output = path.join(root, "renders");
const sizes = [512, 256, 128, 64, 48, 32, 24, 16];

function canvasFragment(content) {
  return Buffer.from(`<svg width="1600" height="1000">${content}</svg>`);
}

function roundedRect(x, y, width, height, radius, fill, stroke = "none") {
  return canvasFragment(
    `<rect x="${x}" y="${y}" width="${width}" height="${height}" rx="${radius}" fill="${fill}" stroke="${stroke}"/>`,
  );
}

function label(text, x, y, size, fill) {
  return canvasFragment(
    `<text x="${x}" y="${y}" font-family="Segoe UI,Arial,sans-serif" font-size="${size}" font-weight="600" text-anchor="middle" fill="${fill}">${text}</text>`,
  );
}

function detailFragment(content) {
  return Buffer.from(`<svg width="1200" height="450">${content}</svg>`);
}

function detailLabel(text, x, y, size, fill) {
  return detailFragment(
    `<text x="${x}" y="${y}" font-family="Segoe UI,Arial,sans-serif" font-size="${size}" font-weight="600" text-anchor="middle" fill="${fill}">${text}</text>`,
  );
}

async function main() {
  fs.mkdirSync(output, { recursive: true });
  const images = {};

  for (const size of sizes) {
    const iconSource = size <= 24 ? smallSource : source;
    const image = await sharp(iconSource)
      .resize(size, size, { kernel: "lanczos3" })
      .png()
      .toBuffer();
    images[size] = image;
    fs.writeFileSync(path.join(output, `javmetalite-icon-e-${size}.png`), image);
  }

  const layers = [
    { input: roundedRect(48, 42, 1504, 916, 34, "#E9EEF5", "#C8D2DF") },
    { input: label("JavMetaLite icon E — flat vector preview", 800, 100, 34, "#172033") },
    { input: roundedRect(92, 142, 676, 522, 28, "#FFFFFF", "#CFD8E5") },
    { input: roundedRect(832, 142, 676, 522, 28, "#0D1117", "#2A3544") },
    { input: label("Light background", 430, 190, 22, "#44546A") },
    { input: label("Dark UI background", 1170, 190, 22, "#93A4B8") },
    { input: images[512], left: 174, top: 196 },
    { input: images[512], left: 914, top: 196 },
    { input: roundedRect(92, 712, 1416, 190, 26, "#1B2330", "#2A3544") },
    { input: label("Actual Windows icon sizes · 24/16 px use a simplified variant", 800, 754, 22, "#F1F5F9") },
  ];

  const previewSizes = [128, 64, 48, 32, 24, 16];
  const centers = [300, 560, 790, 1000, 1190, 1360];
  const baseline = 850;
  previewSizes.forEach((size, index) => {
    const center = centers[index];
    layers.push({
      input: images[size],
      left: Math.round(center - size / 2),
      top: Math.round(baseline - size),
    });
    layers.push({ input: label(`${size} px`, center, 880, 18, "#93A4B8") });
  });

  await sharp({
    create: {
      width: 1600,
      height: 1000,
      channels: 4,
      background: "#F5F7FA",
    },
  })
    .composite(layers)
    .png()
    .toFile(path.join(output, "javmetalite-icon-e-preview.png"));

  const detailSizes = [32, 24, 16];
  const detailLayers = [
    { input: detailFragment('<rect width="1200" height="450" fill="#1B2330"/>') },
    { input: detailLabel("Nearest-neighbor pixel inspection", 600, 48, 24, "#F1F5F9") },
  ];
  const detailCenters = [260, 600, 940];
  for (let index = 0; index < detailSizes.length; index += 1) {
    const size = detailSizes[index];
    const center = detailCenters[index];
    const zoomed = await sharp(images[size])
      .resize(256, 256, { kernel: "nearest" })
      .png()
      .toBuffer();
    detailLayers.push({ input: zoomed, left: center - 128, top: 82 });
    detailLayers.push({ input: detailLabel(`${size} px enlarged`, center, 384, 20, "#93A4B8") });
  }
  await sharp({
    create: {
      width: 1200,
      height: 450,
      channels: 4,
      background: "#1B2330",
    },
  })
    .composite(detailLayers)
    .png()
    .toFile(path.join(output, "javmetalite-icon-e-small-detail.png"));
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
