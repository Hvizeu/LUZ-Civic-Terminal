"use strict";

const assert = require("assert");
const fs = require("fs");
const fsp = fs.promises;
const os = require("os");
const path = require("path");
const test = require("node:test");
const { aggregate, buildSnapshot, readConnection, safeIdentity } = require("../src/snapshot");

test("exports separate immutable packages and a manifest published last", async (t) => {
  const root = await fsp.mkdtemp(path.join(os.tmpdir(), "luz-vortex-"));
  t.after(() => fsp.rm(root, { recursive: true, force: true }));
  const game = path.join(root, "game"), exchange = path.join(root, "exchange"), vortexRoot = path.join(exchange, "vortex-game"), staging = path.join(root, "staging"), executable = path.join(root, "LUZ.exe");
  await Promise.all([fsp.mkdir(game), fsp.mkdir(vortexRoot, { recursive: true }), fsp.mkdir(staging), fsp.writeFile(executable, "fixture")]);
  const descriptor = { Schema: 1, ConnectionId: "1".repeat(32), GameId: "nivalisnights", GameFolder: game, ExchangeRoot: exchange, VortexRoot: vortexRoot, Executable: executable };
  const descriptorFile = path.join(exchange, "connection.json");
  await fsp.writeFile(descriptorFile, JSON.stringify(descriptor));
  assert.deepEqual(await readConnection(descriptorFile), descriptor);
  for (const [folder, text] of [["one", "alpha"], ["two", "beta"]]) {
    const plugin = path.join(staging, folder, "BepInEx", "plugins", folder);
    await fsp.mkdir(plugin, { recursive: true });
    await fsp.writeFile(path.join(plugin, `${folder}.dll`), text);
  }
  const mods = [
    { id: "mod-one", installationPath: "one", state: "installed", attributes: { name: "One", version: "1.0.0", modId: 1, fileId: 10 } },
    { id: "mod-two", installationPath: "two", state: "installed", attributes: { name: "Two", version: "2.0.0", modId: 2, fileId: 20 } }
  ];
  const first = await buildSnapshot({ descriptor, stagingRoot: staging, mods, requestId: "2".repeat(32) });
  assert.equal(first.manifest.Packages.length, 2);
  assert.notEqual(first.manifest.Packages[0].Payload, first.manifest.Packages[1].Payload);
  assert.ok(fs.existsSync(first.manifestPath));
  assert.equal(await fsp.readFile(path.join(exchange, first.manifest.Packages[0].Payload, "BepInEx", "plugins", "one", "one.dll"), "utf8"), "alpha");
  const second = await buildSnapshot({ descriptor, stagingRoot: staging, mods, requestId: "3".repeat(32) });
  assert.equal(second.manifest.Revision, first.manifest.Revision);
  const metadataUpdate = structuredClone(mods);
  metadataUpdate[0].attributes.fileId = 11;
  metadataUpdate[0].attributes.version = "1.0.1";
  const third = await buildSnapshot({ descriptor, stagingRoot: staging, mods: metadataUpdate, requestId: "4".repeat(32) });
  assert.notEqual(third.manifest.Revision, first.manifest.Revision);
});

test("content digest is deterministic and path-sensitive", () => {
  const files = [
    { Path: "b.txt", Length: 1, Sha256: "b".repeat(64) },
    { Path: "A.txt", Length: 1, Sha256: "a".repeat(64) }
  ];
  assert.equal(aggregate(files), aggregate([...files].reverse()));
  assert.notEqual(aggregate(files), aggregate([{ ...files[0], Path: "c.txt" }, files[1]]));
});

test("identity normalization is bounded and rejects an empty identity", () => {
  assert.equal(safeIdentity("My Mod (optional)"), "My-Mod-optional");
  assert.equal(safeIdentity("x".repeat(300)).length, 128);
  assert.throws(() => safeIdentity("***"), /stable bridge identity/);
});

test("rejects a staging path outside the Vortex staging root", async (t) => {
  const root = await fsp.mkdtemp(path.join(os.tmpdir(), "luz-vortex-escape-"));
  t.after(() => fsp.rm(root, { recursive: true, force: true }));
  const staging = path.join(root, "staging"), outside = path.join(root, "outside"), exchange = path.join(root, "exchange");
  await Promise.all([fsp.mkdir(staging), fsp.mkdir(outside), fsp.mkdir(exchange)]);
  await fsp.writeFile(path.join(outside, "bad.dll"), "bad");
  const descriptor = { Schema: 1, ConnectionId: "1".repeat(32), GameId: "nivalisnights", GameFolder: root, ExchangeRoot: exchange, Executable: __filename };
  await assert.rejects(buildSnapshot({ descriptor, stagingRoot: staging, mods: [{ id: "escape", installationPath: "../outside", state: "installed" }] }), /escapes Vortex staging/);
});
