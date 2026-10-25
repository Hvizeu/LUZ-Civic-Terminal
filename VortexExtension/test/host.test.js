"use strict";

// Opt-in contract checks execute source from the installed host, without starting Vortex.
const assert = require("node:assert/strict");
const childProcess = require("node:child_process");
const crypto = require("node:crypto");
const fs = require("node:fs");
const fsp = fs.promises;
const Module = require("node:module");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");
const PromiseBB = require("bluebird");

const asar = process.env.LUZ_VORTEX_ASAR;
const skip = asar ? false : "Set LUZ_VORTEX_ASAR to opt into installed-host source checks";

function hostSources() {
  const archive = fs.readFileSync(asar);
  const header = JSON.parse(archive.subarray(16, 16 + archive.readUInt32LE(12)));
  const entry = header.files["renderer.js.map"];
  const offset = 8 + archive.readUInt32LE(4) + Number(entry.offset);
  const sourceMap = JSON.parse(archive.subarray(offset, offset + entry.size));
  console.log(`Host archive SHA256: ${crypto.createHash("sha256").update(archive).digest("hex")}`);
  return (suffix) => {
    const index = sourceMap.sources.findIndex(name => name.endsWith("/src/" + suffix));
    assert.notEqual(index, -1, `Host source missing: ${suffix}`);
    return sourceMap.sourcesContent[index];
  };
}

function between(source, start, end) {
  const begin = source.indexOf(start), finish = source.indexOf(end, begin + start.length);
  assert.ok(begin >= 0 && finish > begin, `Host contract changed: ${start}`);
  return source.slice(begin, finish);
}

function evaluate(source, expression, globals = {}) {
  const javascript = Module.stripTypeScriptTypes(source, { mode: "strip" });
  return vm.runInNewContext(`${javascript}\n${expression}`, { console, Promise, ...globals });
}

async function fixture(t, sources) {
  const root = await fsp.mkdtemp(path.join(os.tmpdir(), "luz-vortex-host-"));
  const previousLocal = process.env.LOCALAPPDATA;
  process.env.LOCALAPPDATA = path.join(root, "local");
  t.after(async () => { process.env.LOCALAPPDATA = previousLocal; await fsp.rm(root, { recursive: true, force: true }); });
  const gameFolder = path.join(root, "game"), staging = path.join(root, "staging");
  const exchange = path.join(process.env.LOCALAPPDATA, "LUZVortexBridge");
  await fsp.mkdir(gameFolder); await fsp.mkdir(staging);
  await fsp.writeFile(path.join(gameFolder, "Nivalis Nights.exe"), "never execute");
  await fsp.writeFile(path.join(gameFolder, "GameAssembly.dll"), "fixture");
  const state = { app: { appVersion: "2.9.0", extensions: {} }, settings: { gameMode: { discovered: {} } },
    persistent: { mods: { nivalisnights: {} }, profiles: {} } };
  const notifications = [], errors = [], actions = [], downloads = [], ready = [], tools = [], hooks = [], types = [];
  class UserCanceled extends Error {}
  class ProcessCanceled extends Error {}
  let active, game;
  const getSafe = (value, keys, fallback) => keys.reduce((current, key) => current?.[key], value) ?? fallback;
  const reducerSource = sources("extensions/gamemode_management/reducers/settings/settings.ts");
  const toolReducer = evaluate(reducerSource.slice(reducerSource.indexOf("export const settingsReducer:")).replace("export ", ""),
    "settingsReducer.reducers.addDiscoveredTool", {
      actions: new Proxy({}, { get: (target, key) => key }), getSafe,
      _: { omit: (value, keys) => Object.fromEntries(Object.entries(value).filter(([key]) => !keys.includes(key))),
        isEqual: require("node:util").isDeepStrictEqual },
      setSafe(value, keys, result) {
        const copy = structuredClone(value); let cursor = copy;
        for (const key of keys.slice(0, -1)) cursor = cursor[key] ??= {};
        cursor[keys.at(-1)] = result; return copy;
      }
    });
  const api = {
    getState: () => state,
    ext: { ensureLoggedIn: async () => {}, nexusGetModFiles: async () => [
      { file_name: "LUZ-Civic-Terminal-0.8.5-win-x64.zip", version: "0.8.5", category_id: 1, file_id: 123 }
    ] },
    showDialog: async () => ({ action: "Connect LUZ" }),
    showErrorNotification: (title, error) => errors.push({ title, error }),
    sendNotification: value => notifications.push(value), dismissNotification() {}, onAsync() {},
    store: { getState: () => state, dispatch(action) {
      actions.push(action);
      if (action.type === "tool") {
        const [gameId, id, value] = action.args;
        state.settings.gameMode = toolReducer(state.settings.gameMode,
          { gameId, toolId: id, result: value, manual: false });
      }
      if (action.type === "primary") state.settings.interface = { primaryTool: { nivalisnights: action.args[1] } };
      if (action.type === "attribute") {
        const [gameId, id, key, value] = action.args;
        state.persistent.mods[gameId][id].attributes[key] = value;
      }
    } },
    events: { on() {}, emit(event, ...args) {
      const callback = args.find(value => typeof value === "function");
      if (event === "start-download") {
        downloads.push(args[0]);
        assert.ok(active, "Discovery must not install LUZ before a profile is active");
        callback(null, "download");
      } else if (event === "start-install-download") {
        const folder = path.join(staging, "luz"); fs.mkdirSync(folder);
        for (const file of ["LUZ Civic Terminal.exe", "LUZ Vortex.exe"]) fs.writeFileSync(path.join(folder, file), "never execute");
        state.persistent.mods.nivalisnights.luz = { id: "luz", installationPath: "luz", state: "installed", type: "nivalisnights-luz-tool" };
        callback(null, "luz");
      }
    } }
  };
  const selectors = { installPathForGame: () => staging, activeProfile: () => active,
    discoveryByGame: () => state.settings.gameMode.discovered.nivalisnights };
  const vortexApi = { actions: {
    setPrimaryTool: (...args) => ({ type: "primary", args }), setModType: (...args) => ({ type: "type", args }),
    addDiscoveredTool: (...args) => ({ type: "tool", args }), setModAttribute: (...args) => ({ type: "attribute", args })
  }, selectors, log() {}, util: { UserCanceled, ProcessCanceled, GameStoreHelper: { findByAppId: async () => ({ gamePath: gameFolder }) } } };
  const load = Module._load;
  Module._load = function(request, parent, main) { return request === "vortex-api" ? vortexApi : load.call(this, request, parent, main); };
  let extension;
  try { delete require.cache[require.resolve("../src/index")]; extension = require("../src/index"); }
  finally { Module._load = load; }
  const guardSource = between(sources("ExtensionManager.ts"), "class APIProxyHandler ", "class APIProxyCreator ");
  const Guard = evaluate(guardSource, "APIProxyHandler", {
    ExtEventHandler: class { constructor(events) { return events; } }, convertExtInfo: value => value
  });
  const guard = new Guard({}, false, api.events);
  const context = { api: new Proxy(api, guard), requireVersion() {},
    registerGame(value) { game = value; }, registerModType(...args) { types.push(args); }, registerInstaller() {},
    registerStartHook(priority, id, hook) { hooks.push({ priority, id, hook }); }, once(callback) { ready.push(callback); } };
  assert.equal(extension.init(context), true, "Registration must succeed behind the real API guard");
  guard.enable(); for (const callback of ready) await callback();
  const discoverySource = between(sources("extensions/gamemode_management/util/discovery.ts"),
    "export async function quickDiscoveryTools(", "async function ").replace("export ", "");
  const quickTools = evaluate(discoverySource, "quickDiscoveryTools", { autoGenIcon: async () => {},
    log(level, message, details) { if (level === "error") errors.push({ title: message, error: details.err }); } });
  const nativeExecFile = childProcess.execFile;
  childProcess.execFile = (executable, args, options, callback) => {
    assert.equal(executable, path.join(staging, "luz", "LUZ Civic Terminal.exe"));
    assert.equal(args[0], "--vortex-setup");
    const request = JSON.parse(fs.readFileSync(args[1], "utf8"));
    const resultFolder = path.join(exchange, "setup-results"); fs.mkdirSync(resultFolder, { recursive: true });
    fs.writeFileSync(path.join(resultFolder, request.RequestId + ".json"), JSON.stringify({ Outcome: "Applied" }));
    fs.writeFileSync(path.join(exchange, "connection.json"), JSON.stringify({ Schema: 1, SetupComplete: true,
      SnapshotSchemas: [1, 2], Version: "0.8.5", ConnectionId: "a".repeat(32), GameId: "nivalisnights", GameFolder: gameFolder,
      ExchangeRoot: exchange, VortexRoot: path.join(exchange, "vortex-game"), Executable: executable,
      HandoffExecutable: path.join(path.dirname(executable), "LUZ Vortex.exe") }));
    callback(null); return { kill() {} };
  };
  t.after(() => { childProcess.execFile = nativeExecFile; });
  const hookSource = between(sources("ExtensionManager.ts"), "  private applyStartHooks(", "  private runExecutable =")
    .replace("  private applyStartHooks(", "function applyStartHooks(");
  const runHooks = evaluate(hookSource, "applyStartHooks", { PromiseBB, UserCanceled, ProcessCanceled, log() {}, getErrorMessageOrDefault: error => error.message });
  const proxySource = between(sources("extensions/gamemode_management/util/getGame.ts"), "const gameExHandler =", "function makeGameProxy");
  const handler = evaluate(proxySource, "gameExHandler", { path, getModTypeExtensions: () => types.map(type => ({
    typeId: type[0], isSupported: type[2], getPath: type[3]
  })) });
  const primarySource = between(sources("extensions/starter_dashlet/index.ts"), "function testPrimaryTool(", "const onDeploymentEvent");
  const validatePrimary = evaluate(primarySource, "testPrimaryTool", { path, PromiseBB, fs: { statAsync: fsp.stat },
    getSafe, truthy: Boolean, activeGameId: () => active?.gameId, gameName: () => game.name, gameById: () => game,
    setPrimaryTool: (...args) => ({ type: "primary", args }) });
  return { game, state, notifications, errors, downloads, actions, tools, hooks, gameFolder, staging, UserCanceled,
    discover: () => quickTools(game.id, game.supportedTools, (id, tool) => tools.push(tool)),
    activate() { active = { id: "profile", gameId: "nivalisnights" }; state.persistent.profiles.profile = active; },
    paths: root => new Proxy(game, handler).getModPaths(root),
    validatePrimary: () => validatePrimary(api),
    run: request => runHooks.call({ mStartHooks: hooks }, request) };
}

test("installed Vortex lifecycle: cold discovery, setup, separate packages and repeated Play", { skip }, async t => {
  const f = await fixture(t, hostSources());
  await f.discover();
  assert.equal(f.downloads.length, 0, "Game scanning is read-only");
  assert.equal(f.notifications.length, 0, "Game scanning does not start bootstrap");
  assert.equal(f.errors.length, 0);
  const root = await f.game.queryPath();
  f.state.settings.gameMode.discovered.nivalisnights = { path: root };
  f.activate();
  await f.game.setup({ path: root });
  assert.equal(f.downloads.length, 1);
  assert.ok(f.state.settings.gameMode.discovered.nivalisnights.tools?.["luz-civic-terminal"]?.path,
    "Setup must publish the primary tool even when discovery happened before download");
  await f.validatePrimary();
  assert.equal(f.state.settings.interface.primaryTool.nivalisnights, "luz-civic-terminal");
  const unrelated = { executable: path.join(f.staging, "other-tool.exe"), args: ["unchanged"], options: {} };
  assert.equal(await f.run(unrelated), unrelated, "The global hook leaves other tools untouched");
  const paths = f.paths(root);
  for (const destination of Object.values(paths)) {
    assert.ok(path.isAbsolute(destination));
    assert.ok(destination.startsWith(path.dirname(root) + path.sep));
    assert.ok(fs.statSync(destination).isDirectory(), "Every registered destination exists");
  }
  for (const id of ["enabled", "disabled"]) {
    const folder = path.join(f.staging, id, "BepInEx", "plugins", id);
    await fsp.mkdir(folder, { recursive: true }); await fsp.writeFile(path.join(folder, id + ".dll"), id);
    f.state.persistent.mods.nivalisnights[id] = { id, state: "installed", installationPath: id, type: "luz-bridge", attributes: {} };
  }
  const request = { executable: path.join(root, f.game.executable()), args: [], options: {} };
  const first = await f.run(request), second = await f.run({ ...request, executable: first.executable });
  assert.equal(first.executable, path.join(f.staging, "luz", "LUZ Vortex.exe"));
  assert.equal(first.args[0], "--vortex-sync"); assert.equal(first.args[1], second.args[1]);
  const manifest = JSON.parse(await fsp.readFile(first.args[1], "utf8"));
  assert.deepEqual(manifest.Packages.map(item => item.BridgeId).sort(), ["disabled", "enabled"]);
  assert.deepEqual((await fsp.readdir(f.gameFolder)).sort(), ["GameAssembly.dll", "Nivalis Nights.exe"]);
  assert.equal(f.errors.length, 0);
});

test("installed Vortex start chain preserves cancellation and its original reason", { skip }, async t => {
  const f = await fixture(t, hostSources());
  const root = await f.game.queryPath();
  f.state.settings.gameMode.discovered.nivalisnights = { path: root };
  f.activate();
  await f.game.setup({ path: root });
  f.state.settings.gameMode.discovered.nivalisnights.path = f.gameFolder;
  await assert.rejects(f.run({ executable: path.join(f.staging, "luz", "LUZ Vortex.exe"), args: [], options: {} }),
    error => error instanceof f.UserCanceled && /isolated LUZ deployment root/.test(error.message));
});
