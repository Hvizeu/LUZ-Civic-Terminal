"use strict";

const childProcess = require("child_process");
const crypto = require("crypto");
const fs = require("fs");
const fsp = fs.promises;
const path = require("path");
const { actions, selectors, util } = require("vortex-api");
const { GAME_ID, buildSnapshot, readConnection, safeIdentity } = require("./snapshot");

const MOD_TYPE = "luz-bridge";
const LUZ_TOOL_TYPE = "nivalisnights-luz-tool";
const LUZ_EXE = "LUZ Civic Terminal.exe";
const LUZ_PACKAGE_MARKER = "luz-vortex-package.json";
const GAME_EXE = "Nivalis Nights.exe";
const BRIDGE_MARKER = "luz-bridge-game.json";
const STEAM_APP_ID = "1488490";
const MIN_VORTEX = "2.9.0";
const MIN_LUZ = "0.8.5";
const HANDOFF_EXE = "LUZ Vortex.exe";
const EXTENSION_ID = "game-nivalisnights-luz";
let inventoryEpoch = 0;
let pendingHandoff;
let downloadingLuz = false;
let luzDownload;
const resultMonitors = new Map();
const LUZ_NEXUS_MOD_ID = 81;
let exporter = Promise.resolve();
let setupQueue = Promise.resolve();
let actualGameFolder = "";

function exchangeRoot() {
  return path.join(process.env.LOCALAPPDATA || "", "LUZVortexBridge");
}

function descriptorPath() {
  return path.join(exchangeRoot(), "connection.json");
}

function virtualRoot() {
  return path.join(exchangeRoot(), "vortex-game");
}

async function connection() {
  return readConnection(descriptorPath());
}

function samePath(left, right) {
  return path.resolve(left).toLowerCase() === path.resolve(right).toLowerCase();
}

function versionAtLeast(actual, minimum) {
  const parse = (value) => {
    const match = String(value || "").match(/^\s*v?(\d+)\.(\d+)\.(\d+)(?:\.\d+)?(?:[-+][0-9A-Za-z.-]+)?\s*$/);
    return match ? match.slice(1, 4).map(Number) : null;
  };
  const left = parse(actual), right = parse(minimum);
  if (!left || !right) return false;
  for (let index = 0; index < 3; index++) {
    const difference = (left[index] || 0) - (right[index] || 0);
    if (difference !== 0) return difference > 0;
  }
  return true;
}

async function findActualGame() {
  const discovered = await util.GameStoreHelper.findByAppId(STEAM_APP_ID, "steam");
  const game = path.resolve(discovered.gamePath);
  await Promise.all([fsp.access(path.join(game, GAME_EXE)), fsp.access(path.join(game, "GameAssembly.dll"))]);
  actualGameFolder = game;
  return game;
}

async function ensureVirtualRoot() {
  const root = virtualRoot();
  await fsp.mkdir(path.join(root, "deployed"), { recursive: true });
  await fsp.writeFile(path.join(root, BRIDGE_MARKER), JSON.stringify({ Schema: 1, GameId: GAME_ID }, null, 2));
  return root;
}

async function findSystemLuz() {
  try {
    const descriptor = await connection();
    if (versionAtLeast(descriptor.Version, MIN_LUZ)) return descriptor.Executable;
  } catch { /* First-time setup has no descriptor. */ }
  const programs = path.join(process.env.LOCALAPPDATA || "", "Programs", "LUZ Civic Terminal");
  let versions;
  try { versions = await fsp.readdir(programs, { withFileTypes: true }); }
  catch (error) { if (error.code === "ENOENT") return ""; throw error; }
  const folders = versions.filter((entry) => entry.isDirectory() && versionAtLeast(entry.name, MIN_LUZ))
    .sort((left, right) => right.name.localeCompare(left.name, "en", { numeric: true }));
  for (const folder of folders) {
    const executable = path.join(programs, folder.name, LUZ_EXE);
    try { await fsp.access(executable); return executable; } catch { /* Try the next complete installation. */ }
  }
  return "";
}

function installedMods(state) {
  return Object.values(state.persistent?.mods?.[GAME_ID] || {}).filter((mod) => mod && mod.state === "installed");
}

function managedLuzPath(api, requestedId = "") {
  const state = api.getState();
  const staging = selectors.installPathForGame(state, GAME_ID);
  if (!staging) return "";
  const candidates = installedMods(state).filter((item) => requestedId ? item.id === requestedId : item.type === LUZ_TOOL_TYPE);
  if (candidates.length > 1) throw new Error("Multiple Vortex-managed LUZ packages are installed. Keep one active installation before setup.");
  const mod = candidates[0];
  if (!mod?.installationPath) return "";
  const executable = path.join(staging, mod.installationPath, LUZ_EXE);
  return fs.existsSync(executable) ? executable : "";
}

async function latestLuzRelease(api) {
  if (!api.ext?.nexusGetModFiles) throw new Error("This Vortex installation cannot query the LUZ Nexus files");
  const files = await api.ext.nexusGetModFiles(GAME_ID, LUZ_NEXUS_MOD_ID);
  const releases = files.flatMap((file) => {
    const name = String(file.file_name || file.name || "");
    const match = name.match(/^LUZ-Civic-Terminal-([0-9.]+)-win-x64\.zip$/i);
    const version = String(file.version || match?.[1] || "");
    return file.category_id === 1 && match && versionAtLeast(version, MIN_LUZ)
      ? [{ fileId: file.file_id, name, version, uploaded: Number(file.uploaded_timestamp || file.uploaded_time || 0) }]
      : [];
  }).sort((left, right) => right.uploaded - left.uploaded || right.fileId - left.fileId);
  if (releases.length === 0) throw new Error(`LUZ ${MIN_LUZ} or later is not available as the Windows Main File on Nexus`);
  const release = releases[0];
  return { ...release, url: `nxm://${GAME_ID}/mods/${LUZ_NEXUS_MOD_ID}/files/${release.fileId}` };
}

function emitAsync(api, event, ...argumentsBeforeCallback) {
  return new Promise((resolve, reject) => api.events.emit(event, ...argumentsBeforeCallback,
    (error, value) => error ? reject(error) : resolve(value)));
}

async function downloadLuz(context) {
  const api = context.api;
  const notification = `${GAME_ID}-luz-installing`;
  api.sendNotification({ id: notification, type: "activity", message: "Installing LUZ Civic Terminal", noDismiss: true, allowSuppress: false });
  try {
    downloadingLuz = true;
    if (api.ext?.ensureLoggedIn) await api.ext.ensureLoggedIn();
    const release = await latestLuzRelease(api);
    const downloadId = await new Promise((resolve, reject) => api.events.emit("start-download", [release.url],
      { game: GAME_ID, name: `LUZ Civic Terminal ${release.version}` }, undefined,
      (error, value) => error ? reject(error) : resolve(value), undefined, { allowInstall: false }));
    const modId = await emitAsync(api, "start-install-download", downloadId, { allowAutoEnable: false });
    api.store.dispatch(actions.setModType(GAME_ID, modId, LUZ_TOOL_TYPE));
    for (let attempt = 0; attempt < 40; attempt++) {
      const executable = managedLuzPath(api, modId);
      if (executable) return executable;
      await new Promise((resolve) => setTimeout(resolve, 250));
    }
    throw new Error("Vortex installed LUZ but its executable was not found in staging");
  } finally {
    downloadingLuz = false;
    api.dismissNotification?.(notification);
  }
}

async function ensureLuz(context) {
  const system = await findSystemLuz();
  if (system) return system;
  const managed = managedLuzPath(context.api);
  if (managed) return managed;
  if (!luzDownload) luzDownload = downloadLuz(context).finally(() => { luzDownload = undefined; });
  return luzDownload;
}

function run(executable, args) {
  return new Promise((resolve, reject) => childProcess.execFile(executable, args,
    { cwd: path.dirname(executable), windowsHide: true, timeout: 10 * 60 * 1000 },
    (error) => error ? reject(error) : resolve()));
}

async function configureLuz(executable, gameFolder, reviewed = false) {
  const requestId = crypto.randomBytes(16).toString("hex");
  const folder = path.join(exchangeRoot(), "setup");
  const request = path.join(folder, `${requestId}.json`);
  const temporary = `${request}.tmp-${crypto.randomBytes(8).toString("hex")}`;
  await fsp.mkdir(folder, { recursive: true });
  await fsp.writeFile(temporary, JSON.stringify({ Schema: 1, RequestId: requestId, GameId: GAME_ID, GameFolder: gameFolder, Reviewed: reviewed, CreatedUtc: new Date().toISOString() }, null, 2), { flag: "wx" });
  await fsp.rename(temporary, request);
  let executionError;
  try { await run(executable, ["--vortex-setup", request]); }
  catch (error) {
    if (typeof error.code !== "number") throw error;
    executionError = error;
  }
  const resultPath = path.join(exchangeRoot(), "setup-results", `${requestId}.json`);
  for (let attempt = 0; attempt < 2400; attempt++) {
    try {
      const result = JSON.parse(await fsp.readFile(resultPath, "utf8"));
      if (!["Applied", "NoChanges"].includes(result.Outcome)) throw new Error(result.Message || `LUZ setup ended with ${result.Outcome}`);
      const descriptor = await connection();
      if (!descriptor.SetupComplete || !descriptor.SnapshotSchemas?.includes(2)) throw new Error("LUZ connection exists but loader setup or the snapshot protocol is incomplete");
      if (!versionAtLeast(descriptor.Version, MIN_LUZ))
        throw new Error(`LUZ setup requires ${MIN_LUZ} or later; the connected executable reported ${descriptor.Version || "an unknown version"}`);
      if (!samePath(descriptor.GameFolder, gameFolder))
        throw new Error("LUZ setup returned a connection for a different Nivalis installation");
      return descriptor;
    } catch (error) {
      if (error.code !== "ENOENT") throw error;
      await new Promise((resolve) => setTimeout(resolve, 250));
    }
  }
  throw new Error((executionError ? `LUZ setup exited with ${executionError.code}. ` : "") + "LUZ did not return the automatic setup result. Close any older LUZ window once, then retry Play in Vortex.");
}

function queueSetup(task) {
  const next = setupQueue.then(task, task);
  setupQueue = next.catch(() => undefined);
  return next;
}

async function ensureLuzConnection(executable, gameFolder, contextForSetup) {
  try {
    const descriptor = await connection();
    if (versionAtLeast(descriptor.Version, MIN_LUZ) && descriptor.SetupComplete && descriptor.SnapshotSchemas?.includes(2) && samePath(descriptor.Executable, executable) && samePath(descriptor.GameFolder, gameFolder)) return descriptor;
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }
  assertNoConflicts(contextForSetup.api.getState());
  const answer = await contextForSetup.api.showDialog("question", "Connect LUZ for Nivalis Nights", {
    text: `Game: ${gameFolder}\nLUZ: ${executable}\n\nVortex keeps downloads and installer choices. LUZ manages profiles and deployment. If the loader is missing, LUZ downloads and installs the latest official Windows x64 IL2CPP BepInEx build. Existing mods and settings are preserved. Vortex Play opens LUZ and never launches Nivalis.`
  }, [{ label: "Cancel" }, { label: "Connect LUZ" }]);
  if (answer.action !== "Connect LUZ") throw new util.UserCanceled("LUZ setup was cancelled");
  return configureLuz(executable, gameFolder, true);
}

async function directDeploymentEvidence(gameFolder) {
  const roots = [path.join(gameFolder, "BepInEx"), path.join(gameFolder, "winhttp.dll"), path.join(gameFolder, "doorstop_config.ini")];
  let inspected = 0;
  async function inspect(target) {
    let stat;
    try { stat = await fsp.lstat(target); } catch (error) { if (error.code === "ENOENT") return false; throw error; }
    inspected++;
    if (inspected > 25000) throw new Error("The current game deployment is too large to verify safely");
    if (stat.isSymbolicLink() || stat.isFile() && stat.nlink > 1 || path.basename(target).endsWith(".vortex_backup")) return true;
    if (!stat.isDirectory()) return false;
    for (const entry of await fsp.readdir(target)) if (await inspect(path.join(target, entry))) return true;
    return false;
  }
  for (const root of roots) if (await inspect(root)) return true;
  return false;
}

function installedInventory(state) {
  return installedMods(state).filter((mod) => mod.type !== LUZ_TOOL_TYPE);
}

function queueExport(task) {
  const next = exporter.then(task, task);
  exporter = next.catch(() => undefined);
  return next;
}

function assertNoConflicts(state) {
  const conflicts = Object.entries(state.app?.extensions || {}).filter(([key, entry]) => {
    const id = entry.infoJsonId || key;
    return entry.enabled !== false && !entry.remove && id !== EXTENSION_ID
      && (["game-nivalisnights", "game-nivalisnights-luz-bridge"].includes(id) || entry.gameId === 10401 || entry.gameId === GAME_ID);
  });
  if (conflicts.length) throw new Error("Another Nivalis Nights extension is enabled. If it owns a direct deployment, use its Purge Mods action first. Then disable that extension, enable LUZ for Nivalis Nights and restart Vortex. Packages and LUZ profiles are retained.");
}

function assertIdle(state) {
  if (Object.values(state.persistent?.mods?.[GAME_ID] || {}).some(mod => mod.state === "installing"))
    throw new Error("A Nivalis package is still installing. Finish or cancel its installer before Play.");
}

function inventoryFingerprint(state) {
  assertIdle(state);
  return JSON.stringify(installedInventory(state).map(mod => ({ id: mod.id, state: mod.state, path: mod.installationPath, type: mod.type, attributes: mod.attributes })).sort((a, b) => a.id.localeCompare(b.id)));
}

function exchangeDestination(api, area) {
  const target = path.resolve(virtualRoot(), area);
  if (!target.startsWith(path.resolve(exchangeRoot()) + path.sep)) throw new Error("Unsafe exchange destination");
  const state = api.getState();
  const staging = selectors.installPathForGame(state, GAME_ID);
  const descriptor = fs.existsSync(descriptorPath()) ? JSON.parse(fs.readFileSync(descriptorPath(), "utf8")) : {};
  for (const protectedRoot of [actualGameFolder, staging, descriptor.LibraryRoot].filter(Boolean)) {
    const left = path.resolve(target).toLowerCase(), right = path.resolve(protectedRoot).toLowerCase();
    if (left === right || left.startsWith(right + path.sep) || right.startsWith(left + path.sep)) throw new Error("Exchange overlaps game, staging or LUZ library");
  }
  return target;
}

async function exportInventory(context) {
  assertNoConflicts(context.api.getState());
  // Installation state and events establish completion; hashing detects subsequent staging edits.
  const deadline = Date.now() + 5 * 60 * 1000;
  while (Object.values(context.api.getState().persistent?.mods?.[GAME_ID] || {}).some(mod => mod.state === "installing")) {
    if (Date.now() > deadline) throw new Error("A Nivalis installer is still pending. Finish or cancel it, then retry Play.");
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  const descriptor = await connection();
  if (!descriptor.SnapshotSchemas?.includes(2)) throw new Error(`Update LUZ to ${MIN_LUZ} or later before synchronization`);
  const state = context.api.getState();
  const stagingRoot = selectors.installPathForGame(state, GAME_ID);
  if (!stagingRoot) throw new Error("Vortex has no Nivalis Nights staging folder");
  const discovery = selectors.discoveryByGame(state, GAME_ID);
  if (!discovery?.path || !samePath(discovery.path, descriptor.VortexRoot)) throw new Error("Vortex is not using the isolated LUZ deployment root");
  exchangeDestination(context.api, "deployed");
  for (const mod of installedInventory(state)) {
    if (!mod.attributes?.luzBridgeId) context.api.store.dispatch(actions.setModAttribute(GAME_ID, mod.id, "luzBridgeId", safeIdentity(mod.id)));
  }
  const fingerprint = inventoryFingerprint(context.api.getState()), epoch = inventoryEpoch;
  const exported = await buildSnapshot({ descriptor, stagingRoot, mods: structuredClone(installedInventory(context.api.getState())), verifyInventory() {
    if (epoch !== inventoryEpoch || fingerprint !== inventoryFingerprint(context.api.getState())) throw new Error("Vortex inventory changed during export. Retry Play after installation finishes.");
  } });
  if (pendingHandoff?.manifest.ConnectionId === descriptor.ConnectionId && pendingHandoff.manifest.Revision === exported.manifest.Revision) {
    let result;
    try { result = JSON.parse(await fsp.readFile(path.join(descriptor.ExchangeRoot, "results", `${pendingHandoff.manifest.RequestId}.json`), "utf8")); }
    catch (error) { if (error.code !== "ENOENT") throw error; }
    if (!result || !queueIsTerminal(result.Outcome)) {
      await fsp.rm(path.dirname(exported.manifestPath), { recursive: true, force: true });
      return { descriptor, ...pendingHandoff };
    }
  }
  pendingHandoff = exported;
  return { descriptor, ...exported };
}

function queueIsTerminal(outcome) {
  return !["Queued", "Processing"].includes(outcome);
}

function monitorResult(context, descriptor, requestId) {
  const file = path.join(descriptor.ExchangeRoot, "results", `${requestId}.json`);
  if (resultMonitors.has(requestId)) return;
  const started = Date.now();
  const timer = setInterval(async () => {
    if (Date.now() - started > 10 * 60 * 1000) { clearInterval(timer); resultMonitors.delete(requestId); context.api.sendNotification({ id: `luz-bridge-${requestId}`, type: "warning", title: "LUZ result still pending", message: "Open LUZ to review this request. A timeout does not mean it was applied." }); return; }
    try {
      const result = JSON.parse(await fsp.readFile(file, "utf8"));
      if (!queueIsTerminal(result.Outcome)) return;
      clearInterval(timer); resultMonitors.delete(requestId);
      context.api.sendNotification({ id: `luz-bridge-${requestId}`,
        type: result.Outcome === "Applied" || result.Outcome === "NoChanges" ? "success" : "warning",
        title: `LUZ: ${result.Outcome}`, message: result.Message || "LUZ synchronization completed." });
    } catch (error) {
      if (error.code !== "ENOENT") { clearInterval(timer); context.api.showErrorNotification?.("Could not read the LUZ synchronization result", error); }
    }
  }, 1000);
  resultMonitors.set(requestId, timer);
  timer.unref?.();
}

async function setup(context, discovery) {
  assertNoConflicts(context.api.getState());
  const vortexVersion = context.api.getState().app?.appVersion;
  if (!versionAtLeast(vortexVersion, MIN_VORTEX)) throw new Error(`LUZ integration requires Vortex ${MIN_VORTEX} or later; found ${vortexVersion || "an unknown version"}`);
  if (!discovery?.path || !samePath(discovery.path, virtualRoot())) throw new Error("Vortex did not select the isolated LUZ deployment root");
  const game = actualGameFolder || await findActualGame();
  if (await directDeploymentEvidence(game)) throw new Error("This installation still contains a previous direct Vortex deployment. Use Purge Mods once, then Manage Nivalis Nights again. LUZ will preserve the installed mod packages.");
  const executable = await ensureLuz(context);
  await queueSetup(() => ensureLuzConnection(executable, game, context));
  context.api.store.dispatch(actions.setPrimaryTool(GAME_ID, "luz-civic-terminal"));
}

function toolArchiveRoot(files) {
  const executable = files.find((file) => path.basename(String(file)).toLowerCase() === LUZ_EXE.toLowerCase());
  if (!executable) return null;
  const parent = path.dirname(String(executable).replace(/\\/g, "/"));
  const root = parent === "." ? "" : parent.replace(/\/$/, "") + "/";
  const marker = (root + LUZ_PACKAGE_MARKER).toLowerCase();
  return files.some((file) => String(file).replace(/\\/g, "/").toLowerCase() === marker) ? root : null;
}

function looksLikeLuzArchive(files) {
  const identities = new Set([
    LUZ_PACKAGE_MARKER.toLowerCase(), LUZ_EXE.toLowerCase(), "luz civic terminal.dll",
    "luz civic terminal.deps.json", "luz civic terminal.runtimeconfig.json"
  ]);
  return files.some((file) => identities.has(path.basename(String(file)).toLowerCase()));
}

async function installLuzPackage(files, destinationPath) {
  const root = toolArchiveRoot(files);
  if (root === null) throw new Error("The official LUZ package marker or executable is missing");
  const marker = path.resolve(destinationPath, ...(`${root}${LUZ_PACKAGE_MARKER}`).split("/"));
  const boundary = path.resolve(destinationPath) + path.sep;
  if (!marker.startsWith(boundary)) throw new Error("The LUZ package marker escapes the extracted archive");
  const metadata = JSON.parse(await fsp.readFile(marker, "utf8"));
  if (metadata.schema !== 1 || metadata.product !== "LUZ Civic Terminal" || metadata.gameId !== GAME_ID
      || metadata.platform !== "win-x64" || !versionAtLeast(metadata.version, MIN_LUZ))
    throw new Error(`This is not a supported LUZ ${MIN_LUZ} or later Windows package`);
  return { instructions: [
    ...files.filter((file) => !/[\\/]$/.test(String(file)) && String(file).replace(/\\/g, "/").startsWith(root))
      .map((file) => ({ type: "copy", source: file, destination: String(file).replace(/\\/g, "/").slice(root.length) })),
    { type: "setmodtype", value: LUZ_TOOL_TYPE }
  ] };
}

async function configureInstalledLuz(context, gameId, modId) {
  if (downloadingLuz) return false; // The bootstrap caller owns this setup and its single review.
  if (gameId !== GAME_ID) return false;
  const mod = installedMods(context.api.getState()).find((item) => item.id === modId);
  if (mod?.type !== LUZ_TOOL_TYPE) return false;
  const executable = managedLuzPath(context.api, modId);
  if (!executable) return false;
  context.api.store.dispatch(actions.setModType(GAME_ID, modId, LUZ_TOOL_TYPE));
  const game = actualGameFolder || await findActualGame();
  await queueSetup(() => ensureLuzConnection(executable, game, context));
  context.api.store.dispatch(actions.setPrimaryTool(GAME_ID, "luz-civic-terminal"));
  context.api.sendNotification({ id: `${GAME_ID}-luz-ready`, type: "success", title: "LUZ connected",
    message: "LUZ and its loader are connected. Install or update mods, then press Play to synchronize and review the first Apply." });
  return true;
}

function init(context) {
  try { assertNoConflicts(context.api.getState()); }
  catch (error) { context.once(() => context.api.showErrorNotification("LUZ extension conflict", error)); return false; }
  context.registerGame({
    id: GAME_ID,
    name: "Nivalis Nights",
    mergeMods: (mod) => safeIdentity(mod?.id || mod?.installationPath),
    requiresCleanup: true,
    queryPath: async () => { await findActualGame(); return ensureVirtualRoot(); },
    executable: () => BRIDGE_MARKER,
    requiredFiles: [BRIDGE_MARKER],
    queryModPath: () => exchangeDestination(context.api, "deployed"),
    setup: (discovery) => setup(context, discovery),
    environment: { SteamAPPId: STEAM_APP_ID },
    details: { steamAppId: +STEAM_APP_ID, nexusPageId: GAME_ID },
    supportedTools: [{
      id: "luz-civic-terminal", name: "LUZ Civic Terminal", shortName: "LUZ",
      queryPath: async () => path.dirname(await ensureLuz(context)), executable: () => HANDOFF_EXE,
      requiredFiles: [HANDOFF_EXE], relative: false, exclusive: false, defaultPrimary: true
    }]
  });

  context.registerModType(MOD_TYPE, 5, (gameId) => gameId === GAME_ID,
    () => exchangeDestination(context.api, "deployed"), () => Promise.resolve(true), { name: "LUZ exchange package" });
  context.registerModType(LUZ_TOOL_TYPE, 4, (gameId) => gameId === GAME_ID,
    () => exchangeDestination(context.api, "tools/LUZ"), () => Promise.resolve(false), { name: "LUZ Civic Terminal" });

  context.registerInstaller(`${GAME_ID}-luz-tool`, 5,
    async (files, gameId) => ({ supported: gameId === GAME_ID && looksLikeLuzArchive(files), requiredFiles: [] }),
    installLuzPackage);

  context.registerInstaller(`${GAME_ID}-luz-package`, 90,
    async (files, gameId) => ({ supported: gameId === GAME_ID && !looksLikeLuzArchive(files) && files.some((file) => !/[\\/]$/.test(String(file))), requiredFiles: [] }),
    async (files) => ({ instructions: [
      ...files.filter((file) => !/[\\/]$/.test(String(file))).map((file) => ({ type: "copy", source: file, destination: file })),
      { type: "setmodtype", value: MOD_TYPE }
    ] }));

  context.once(() => {
    for (const event of ["did-install-mod", "did-remove-mod", "did-remove-mods"]) context.api.events.on(event, gameId => { if (gameId === GAME_ID) inventoryEpoch++; });
    context.api.onAsync("will-install-mod", async gameId => { if (gameId === GAME_ID) inventoryEpoch++; });
    context.api.onAsync("will-deploy", async profileId => {
      const state = context.api.getState();
      if (state.persistent?.profiles?.[profileId]?.gameId !== GAME_ID) return;
      assertNoConflicts(state); exchangeDestination(context.api, "deployed");
      const discovery = selectors.discoveryByGame(state, GAME_ID);
      if (!discovery?.path || !samePath(discovery.path, virtualRoot())) throw new Error("Purge the old direct deployment before switching to LUZ exchange mode");
      const paths = util.getGame(GAME_ID).getModPaths(discovery.path);
      for (const target of Object.values(paths)) if (!path.isAbsolute(target) || !path.resolve(target).toLowerCase().startsWith(path.resolve(exchangeRoot()).toLowerCase() + path.sep)) throw new Error("A mod type targets outside the LUZ exchange. Resolve the conflicting game support before deployment.");
    });
    connection().then(async descriptor => {
      const folder = path.join(descriptor.ExchangeRoot, "results");
      for (const file of await fsp.readdir(folder)) {
        if (!/^[a-f0-9]{32}\.json$/.test(file)) continue;
        const result = JSON.parse(await fsp.readFile(path.join(folder, file), "utf8"));
        if (!queueIsTerminal(result.Outcome)) monitorResult(context, descriptor, result.RequestId);
      }
    }).catch(error => { if (error.code !== "ENOENT") context.api.showErrorNotification("LUZ completion monitoring needs attention", error); });
    context.api.events.on("did-install-mod", (gameId, archiveId, modId) =>
      configureInstalledLuz(context, gameId, modId).catch((error) =>
        context.api.showErrorNotification?.("LUZ was installed but automatic setup failed", error)));
  });

  context.registerStartHook(5, "nivalisnights-luz-synchronize", async (runRequest) => {
    const state = context.api.getState();
    if (selectors.activeProfile(state)?.gameId !== GAME_ID) return runRequest;
    const discovery = selectors.discoveryByGame(state, GAME_ID);
    const marker = discovery?.path ? path.join(discovery.path, BRIDGE_MARKER) : "";
    const tool = path.basename(runRequest.executable).toLowerCase() === HANDOFF_EXE.toLowerCase();
    if ((!marker || !samePath(runRequest.executable, marker)) && !tool) return runRequest;
    assertNoConflicts(state);
    try {
      const game = actualGameFolder || await findActualGame();
      const executable = await ensureLuz(context);
      await queueSetup(() => ensureLuzConnection(executable, game, context));
      const exported = await queueExport(() => exportInventory(context));
      monitorResult(context, exported.descriptor, exported.manifest.RequestId);
      context.api.sendNotification({ id: `luz-bridge-${exported.manifest.RequestId}`, type: "activity", title: "Synchronizing with LUZ", message: "LUZ is validating and applying the completed Vortex packages." });
      const handoff = exported.descriptor.HandoffExecutable;
      if (!handoff || !samePath(handoff, path.join(path.dirname(executable), HANDOFF_EXE))) throw new Error("Update the connected LUZ installation: its handoff launcher is missing or mismatched");
      await fsp.access(handoff);
      return { executable: handoff, args: ["--vortex-sync", exported.manifestPath], options: { ...(runRequest.options || {}), cwd: path.dirname(executable), shell: false } };
    } catch (error) {
      context.api.showErrorNotification?.("Nivalis Nights could not hand off to LUZ", error);
      throw new util.UserCanceled(`LUZ stopped Play: ${error.message || error}`);
    }
  });

  context.api.log?.("info", `Automatic LUZ integration registered for ${GAME_ID}; minimum Vortex ${MIN_VORTEX}, minimum LUZ ${MIN_LUZ}`);
  return true;
}

module.exports = { default: init, init, _test: { configureInstalledLuz, configureLuz, downloadLuz, ensureVirtualRoot, findActualGame, installLuzPackage, installedInventory, latestLuzRelease, looksLikeLuzArchive, toolArchiveRoot, versionAtLeast, assertNoConflicts, inventoryFingerprint, exchangeDestination } };
