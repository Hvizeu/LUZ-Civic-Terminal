"use strict";

const crypto = require("crypto");
const fs = require("fs");
const fsp = fs.promises;
const path = require("path");

const GAME_ID = "nivalisnights";
const MAX_FILES = 20000;
const MAX_BYTES = 1024 * 1024 * 1024;

function safeIdentity(value) {
  const cleaned = String(value || "").replace(/[^A-Za-z0-9._-]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 128);
  if (!/^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/.test(cleaned)) throw new Error("Vortex mod has no stable bridge identity");
  return cleaned;
}

function normalizeRelative(value) {
  const normalized = String(value).replace(/\\/g, "/");
  if (!normalized || path.posix.isAbsolute(normalized) || normalized.split("/").some((part) => !part || part === "." || part === ".." || /[<>|?*:\x00-\x1f]/.test(part) || /[. ]$/.test(part)))
    throw new Error(`Unsafe staged path: ${value}`);
  return normalized;
}

async function hashFile(file) {
  const hash = crypto.createHash("sha256");
  await new Promise((resolve, reject) => {
    const stream = fs.createReadStream(file);
    stream.on("data", (chunk) => hash.update(chunk));
    stream.on("error", reject);
    stream.on("end", resolve);
  });
  return hash.digest("hex");
}

async function listFiles(root) {
  const output = [];
  async function visit(folder) {
    const entries = await fsp.readdir(folder, { withFileTypes: true });
    entries.sort((a, b) => a.name.localeCompare(b.name, "en", { sensitivity: "base" }));
    for (const entry of entries) {
      const absolute = path.join(folder, entry.name);
      const stat = await fsp.lstat(absolute);
      if (stat.isSymbolicLink()) throw new Error(`Links are not supported in Vortex staging: ${absolute}`);
      if (stat.isDirectory()) await visit(absolute);
      else if (stat.isFile()) output.push({ absolute, relative: normalizeRelative(path.relative(root, absolute)), size: stat.size, mtimeMs: stat.mtimeMs });
      else throw new Error(`Unsupported staged entry: ${absolute}`);
      if (output.length > MAX_FILES) throw new Error(`Package exceeds the ${MAX_FILES.toLocaleString()} file limit`);
    }
  }
  await visit(root);
  if (output.length === 0) throw new Error(`Installed package is empty: ${root}`);
  if (output.reduce((sum, file) => sum + file.size, 0) > MAX_BYTES) throw new Error("Package exceeds the 1 GB limit");
  return output;
}

function aggregate(files) {
  const value = (file, lower, upper) => file[lower] ?? file[upper];
  const text = [...files].sort((a, b) => {
    const left = String(value(a, "path", "Path")).toLowerCase(), right = String(value(b, "path", "Path")).toLowerCase();
    return left < right ? -1 : left > right ? 1 : 0;
  }).map((file) => `${String(value(file, "path", "Path")).replace(/\\/g, "/")}\0${value(file, "length", "Length")}\0${String(value(file, "sha256", "Sha256")).toLowerCase()}\n`).join("");
  return crypto.createHash("sha256").update(text, "utf8").digest("hex");
}

async function copyPackage(source, destination) {
  const inputs = await listFiles(source);
  const files = [];
  for (const input of inputs) {
    const before = await hashFile(input.absolute);
    const target = path.join(destination, ...input.relative.split("/"));
    await fsp.mkdir(path.dirname(target), { recursive: true });
    await fsp.copyFile(input.absolute, target);
    const [after, copied, stat] = await Promise.all([hashFile(input.absolute), hashFile(target), fsp.stat(input.absolute)]);
    if (before !== after || before !== copied || stat.size !== input.size || stat.mtimeMs !== input.mtimeMs)
      throw new Error(`Vortex staging changed during export: ${input.relative}`);
    files.push({ Path: input.relative, Length: input.size, Sha256: copied });
  }
  const finalInputs = await listFiles(source);
  const fingerprint = (items) => items.map((item) => `${item.relative}\0${item.size}\0${item.mtimeMs}`).join("\n");
  if (fingerprint(inputs) !== fingerprint(finalInputs)) throw new Error(`Vortex staging changed during export: ${source}`);
  return { files, contentSha256: aggregate(files) };
}

function modMetadata(mod, bridgeId) {
  const attributes = mod.attributes || {};
  const sourceModId = attributes.modId ?? attributes.nexusModId ?? mod.modId ?? "";
  const sourceFileId = attributes.fileId ?? attributes.nexusFileId ?? "";
  return {
    BridgeId: bridgeId,
    Name: String(attributes.name || mod.id || bridgeId).slice(0, 240),
    Version: String(attributes.version || mod.version || "0.0.0").slice(0, 80),
    SourceModId: String(sourceModId || ""),
    SourceFileId: String(sourceFileId || ""),
    Variant: String(attributes.variant || "").slice(0, 160),
    PageUrl: sourceModId ? `https://www.nexusmods.com/${GAME_ID}/mods/${sourceModId}` : ""
  };
}

async function readConnection(file) {
  const descriptor = JSON.parse(await fsp.readFile(file, "utf8"));
  if (descriptor.Schema !== 1 || descriptor.GameId !== GAME_ID || !/^[a-f0-9]{32}$/.test(descriptor.ConnectionId || "")) throw new Error("LUZ connection descriptor is incompatible");
  for (const field of ["GameFolder", "ExchangeRoot", "VortexRoot", "Executable"]) if (!path.isAbsolute(descriptor[field] || "")) throw new Error(`LUZ connection has an invalid ${field}`);
  await Promise.all([fsp.access(descriptor.GameFolder), fsp.access(descriptor.ExchangeRoot), fsp.access(descriptor.VortexRoot), fsp.access(descriptor.Executable)]);
  return descriptor;
}

async function buildSnapshot({ descriptor, stagingRoot, mods, requestId = crypto.randomBytes(16).toString("hex") }) {
  if (!/^[a-f0-9]{32}$/.test(requestId)) throw new Error("Invalid snapshot request identity");
  const snapshots = path.join(descriptor.ExchangeRoot, "snapshots");
  const building = path.join(snapshots, `.building-${requestId}`);
  const published = path.join(snapshots, requestId);
  await fsp.rm(building, { recursive: true, force: true });
  await fsp.mkdir(path.join(building, "packages"), { recursive: true });
  const packages = [];
  const identities = new Set();
  try {
    for (const mod of [...mods].sort((a, b) => String(a.id).localeCompare(String(b.id)))) {
      const bridgeId = safeIdentity(mod.id || mod.installationPath);
      if (identities.has(bridgeId.toLowerCase())) throw new Error(`Duplicate Vortex package identity: ${bridgeId}`);
      identities.add(bridgeId.toLowerCase());
      if (!mod.installationPath) throw new Error(`Installed Vortex package ${bridgeId} has no staging path`);
      const source = path.resolve(stagingRoot, mod.installationPath);
      const relative = path.relative(path.resolve(stagingRoot), source);
      if (!relative || relative.startsWith("..") || path.isAbsolute(relative)) throw new Error(`Package staging path escapes Vortex staging: ${bridgeId}`);
      const destination = path.join(building, "packages", bridgeId);
      const copied = await copyPackage(source, destination);
      packages.push({
        ...modMetadata(mod, bridgeId),
        Payload: `snapshots/${requestId}/packages/${bridgeId}`,
        ContentSha256: copied.contentSha256,
        Files: copied.files
      });
    }
    const revisionText = packages.map((item) => `${item.BridgeId}\0${item.ContentSha256}\0${item.SourceModId}\0${item.SourceFileId}\0${item.Version}\0${item.Variant}\n`).join("");
    const manifest = {
      Schema: 1,
      RequestId: requestId,
      ConnectionId: descriptor.ConnectionId,
      GameId: GAME_ID,
      GameFolder: descriptor.GameFolder,
      Revision: crypto.createHash("sha256").update(revisionText, "utf8").digest("hex"),
      CreatedUtc: new Date().toISOString(),
      Packages: packages
    };
    await fsp.rm(published, { recursive: true, force: true });
    await fsp.mkdir(snapshots, { recursive: true });
    await fsp.rename(building, published);
    const manifestPath = path.join(published, "snapshot.json");
    const temporary = `${manifestPath}.tmp-${crypto.randomBytes(8).toString("hex")}`;
    await fsp.writeFile(temporary, JSON.stringify(manifest, null, 2), { flag: "wx" });
    await fsp.rename(temporary, manifestPath);
    return { manifestPath, manifest };
  } catch (error) {
    await fsp.rm(building, { recursive: true, force: true }).catch(() => undefined);
    throw error;
  }
}

module.exports = { GAME_ID, aggregate, buildSnapshot, copyPackage, readConnection, safeIdentity };
