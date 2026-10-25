"use strict";

const assert = require("assert");
const childProcess = require("child_process");
const fs = require("fs");
const fsp = fs.promises;
const Module = require("module");
const os = require("os");
const path = require("path");
const test = require("node:test");

test("discovers Nivalis, pairs LUZ automatically, and replaces Play without game writes", async (t) => {
  const root = await fsp.mkdtemp(path.join(os.tmpdir(), "luz-vortex-extension-"));
  t.after(() => fsp.rm(root, { recursive: true, force: true }));
  const previousLocal = process.env.LOCALAPPDATA;
  process.env.LOCALAPPDATA = path.join(root, "local");
  t.after(() => { process.env.LOCALAPPDATA = previousLocal; });

  const state = { app: { appVersion: "2.9.0" }, staging: path.join(root, "staging"), game: path.join(root, "game"),
    persistent: { mods: { nivalisnights: {} }, profiles: { active: { gameId: "nivalisnights" } } } };
  const exchange = path.join(process.env.LOCALAPPDATA, "LUZVortexBridge");
  const vortexRoot = path.join(exchange, "vortex-game");
  const executable = path.join(process.env.LOCALAPPDATA, "Programs", "LUZ Civic Terminal", "0.8.5", "LUZ Civic Terminal.exe");
  await Promise.all([fsp.mkdir(state.staging, { recursive: true }), fsp.mkdir(state.game, { recursive: true }), fsp.mkdir(path.dirname(executable), { recursive: true })]);
  await fsp.writeFile(path.join(path.dirname(executable), "LUZ Vortex.exe"), "handoff fixture");
  await Promise.all([fsp.writeFile(executable, "fixture; never execute"), fsp.writeFile(path.join(state.game, "Nivalis Nights.exe"), "do not launch"), fsp.writeFile(path.join(state.game, "GameAssembly.dll"), "fixture")]);
  for (const id of ["enabled-mod", "disabled-mod"]) {
    const folder = path.join(state.staging, id, "BepInEx", "plugins", id);
    await fsp.mkdir(folder, { recursive: true });
    await fsp.writeFile(path.join(folder, `${id}.dll`), id);
    state.persistent.mods.nivalisnights[id] = { id, installationPath: id, state: "installed", type: "luz-bridge", attributes: { name: id, version: "1.0.0", modId: id === "enabled-mod" ? 1 : 2, fileId: 10 } };
  }
  const toolFolder = path.join(state.staging, "luz-tool");
  await fsp.mkdir(toolFolder, { recursive: true });
  await fsp.writeFile(path.join(toolFolder, "LUZ Civic Terminal.exe"), "tool payload");
  state.persistent.mods.nivalisnights["luz-tool"] = { id: "luz-tool", installationPath: "luz-tool", state: "installed", type: "nivalisnights-luz-tool", attributes: { name: "LUZ" } };

  const selectors = {
    installPathForGame: () => state.staging,
    discoveryByGame: () => ({ path: vortexRoot }),
    activeProfile: () => ({ gameId: "nivalisnights" }),
    lastActiveProfileForGame: () => "active"
  };
  class UserCanceled extends Error {}
  const vortexApi = {
    actions: { setModType: () => ({ type: "type" }), addDiscoveredTool: () => ({ type: "tool" }), setPrimaryTool: () => ({ type: "primary" }), setModAttribute: () => ({ type: "attribute" }) },
    selectors,
    util: { UserCanceled, GameStoreHelper: { findByAppId: async () => ({ gamePath: state.game }) } }
  };
  const originalLoad = Module._load;
  Module._load = function(request, parent, isMain) { return request === "vortex-api" ? vortexApi : originalLoad.call(this, request, parent, isMain); };
  const originalExecFile = childProcess.execFile;
  childProcess.execFile = function(program, args, options, callback) {
    assert.equal(program, executable);
    assert.equal(args[0], "--vortex-setup");
    const request = JSON.parse(fs.readFileSync(args[1], "utf8"));
    fs.mkdirSync(path.join(exchange, "setup-results"), { recursive: true });
    fs.writeFileSync(path.join(exchange, "setup-results", `${request.RequestId}.json`), JSON.stringify({ Schema: 1, RequestId: request.RequestId, Outcome: "Applied", Message: "connected" }));
    fs.writeFileSync(path.join(exchange, "connection.json"), JSON.stringify({ SnapshotSchemas: [1, 2], SetupComplete: true, HandoffExecutable: path.join(path.dirname(executable), "LUZ Vortex.exe"), Schema: 1, ConnectionId: "a".repeat(32), GameId: "nivalisnights", GameFolder: state.game, ExchangeRoot: exchange, VortexRoot: vortexRoot, Executable: executable, Version: "0.8.5" }));
    callback(null, "", "");
    return { kill() {} };
  };
  t.after(() => { Module._load = originalLoad; childProcess.execFile = originalExecFile; });
  delete require.cache[require.resolve("../src/index")];
  const extension = require("../src/index");
  Module._load = originalLoad;

  let game, startHook;
  const modTypes = [], installers = [], readyCallbacks = [];
  let runtimeReady = false;
  const context = {
    registerGame(value) { game = value; },
    registerStartHook(priority, id, callback) { startHook = { priority, id, callback }; },
    registerModType(...args) { modTypes.push(args); },
    registerInstaller(...args) { installers.push(args); },
    once(callback) { readyCallbacks.push(callback); },
    api: new Proxy({ getState: () => state, events: { emit() {} }, store: { dispatch() {} }, showDialog: async () => ({ action: "Connect LUZ" }), sendNotification() {}, dismissNotification() {}, showErrorNotification() {}, log() {}, onAsync() {}, events: { emit() {}, on() {} } }, {
      get(target, property) {
        if (!runtimeReady) throw new Error("extension uses api in init function");
        return Reflect.get(target, property);
      }
    })
  };
  assert.equal(extension.init(context), true);
  runtimeReady = true;
  for (const callback of readyCallbacks) callback();
  assert.equal(game.logo, "gameart.png");
  assert.equal(game.id, "nivalisnights");
  assert.equal(game.name, "Nivalis Nights");
  assert.equal(game.mergeMods({ id: "separate-package" }), "separate-package");
  assert.equal(await game.queryPath(), vortexRoot);
  assert.equal(game.queryModPath(), path.join(vortexRoot, "deployed"));
  assert.equal(game.supportedTools[0].exclusive, false);
  assert.equal(game.supportedTools[0].executable(), "LUZ Vortex.exe");
  for (const modType of modTypes) assert.ok(path.isAbsolute(modType[3]()));
  assert.ok(fs.existsSync(path.join(vortexRoot, "luz-bridge-game.json")));
  assert.deepEqual(modTypes.map((value) => value[0]), ["luz-bridge", "nivalisnights-luz-tool"]);

  const toolInstaller = installers.find((value) => value[0].endsWith("-luz-tool"));
  const ordinaryInstaller = installers.find((value) => value[0].endsWith("-luz-package"));
  assert.equal(toolInstaller[1], 5);
  assert.equal(ordinaryInstaller[1], 90);
  const toolFiles = ["release/LUZ Civic Terminal.exe", "release/Core.dll", "release/luz-vortex-package.json"];
  const extracted = path.join(root, "extracted");
  await fsp.mkdir(path.join(extracted, "release"), { recursive: true });
  await fsp.writeFile(path.join(extracted, "release", "luz-vortex-package.json"), JSON.stringify({ schema: 1, product: "LUZ Civic Terminal", gameId: "nivalisnights", version: "0.8.5", platform: "win-x64" }));
  assert.equal((await toolInstaller[2](toolFiles, "nivalisnights")).supported, true);
  const toolInstructions = await toolInstaller[3](toolFiles, extracted);
  assert.ok(toolInstructions.instructions.some((instruction) => instruction.type === "setmodtype" && instruction.value === "nivalisnights-luz-tool"));
  assert.ok(toolInstructions.instructions.some((instruction) => instruction.destination === "LUZ Civic Terminal.exe"));
  await fsp.writeFile(path.join(extracted, "release", "luz-vortex-package.json"), JSON.stringify({ schema: 1, product: "LUZ Civic Terminal", gameId: "nivalisnights", version: "0.8.1", platform: "win-x64" }));
  await assert.rejects(toolInstaller[3](toolFiles, extracted), /supported LUZ 0\.8\.5/);
  assert.equal((await ordinaryInstaller[2](["BepInEx/plugins/example.dll"], "nivalisnights")).supported, true);
  const brokenTool = ["release/LUZ Civic Terminal.exe", "release/Core.dll"];
  assert.equal((await toolInstaller[2](brokenTool, "nivalisnights")).supported, true);
  assert.equal((await ordinaryInstaller[2](brokenTool, "nivalisnights")).supported, false);
  await assert.rejects(toolInstaller[3](brokenTool, extracted), /marker or executable is missing/);
  const selected = [...installers].sort((left, right) => left[1] - right[1]);
  const claimed = [];
  for (const installer of selected) if ((await installer[2](toolFiles, "nivalisnights")).supported) claimed.push(installer[0]);
  assert.deepEqual(claimed, ["nivalisnights-luz-tool"]);

  const oldDeployment = path.join(state.game, "BepInEx", "plugins");
  await fsp.mkdir(oldDeployment, { recursive: true });
  const oldSource = path.join(oldDeployment, "old.dll"), oldLink = path.join(oldDeployment, "old-linked.dll");
  await fsp.writeFile(oldSource, "old Vortex deployment");
  await fsp.link(oldSource, oldLink);
  await assert.rejects(game.setup({ path: vortexRoot }), /Purge Mods once/);
  await fsp.rm(path.join(state.game, "BepInEx"), { recursive: true, force: true });
  await game.setup({ path: vortexRoot });

  const beforeGame = (await fsp.readdir(state.game)).sort();
  const replaced = await startHook.callback({ executable: path.join(vortexRoot, "luz-bridge-game.json"), args: [], options: {} });
  assert.equal(replaced.executable, path.join(path.dirname(executable), "LUZ Vortex.exe"));
  const repeated = await startHook.callback({ executable: replaced.executable, args: [], options: {} });
  assert.equal(repeated.args[0], "--vortex-sync");
  assert.equal(repeated.args[1], replaced.args[1], "repeated Play follows the same pending request instead of admitting another writer");
  assert.equal(replaced.args[0], "--vortex-sync");
  const snapshot = JSON.parse(await fsp.readFile(replaced.args[1], "utf8"));
  assert.deepEqual(snapshot.Packages.map((item) => item.BridgeId).sort(), ["disabled-mod", "enabled-mod"]);
  assert.deepEqual((await fsp.readdir(state.game)).sort(), beforeGame);
});

test("validates versions and LUZ release archive routing", async () => {
  const originalLoad = Module._load;
  Module._load = function(request, parent, isMain) {
    return request === "vortex-api"
      ? { actions: {}, selectors: {}, util: { GameStoreHelper: {} } }
      : originalLoad.call(this, request, parent, isMain);
  };
  delete require.cache[require.resolve("../src/index")];
  const helpers = require("../src/index")._test;
  Module._load = originalLoad;
  assert.equal(helpers.versionAtLeast("0.8.5", "0.8.5"), true);
  assert.equal(helpers.versionAtLeast("0.8.1", "0.8.5"), false);
  assert.equal(helpers.versionAtLeast("garbage0.8.5", "0.8.5"), false);
  assert.equal(helpers.versionAtLeast("0.82", "0.8.5"), false);
  assert.equal(helpers.toolArchiveRoot(["LUZ Civic Terminal.exe", "luz-vortex-package.json"]), "");
  assert.equal(helpers.toolArchiveRoot(["folder/LUZ Civic Terminal.exe", "folder/Core.dll", "folder/luz-vortex-package.json"]), "folder/");
  assert.equal(helpers.toolArchiveRoot(["folder/LUZ Civic Terminal.exe", "folder/Core.dll"]), null);
  assert.equal(helpers.toolArchiveRoot(["BepInEx/plugins/mod.dll"]), null);
  assert.equal(helpers.looksLikeLuzArchive(["folder/LUZ Civic Terminal.exe"]), true);
  assert.equal(helpers.looksLikeLuzArchive(["folder/luz-vortex-package.json"]), true);
  assert.equal(helpers.looksLikeLuzArchive(["BepInEx/plugins/mod.dll"]), false);
});

test("downloads and installs LUZ from Nexus through Vortex events", async (t) => {
  const root = await fsp.mkdtemp(path.join(os.tmpdir(), "luz-vortex-download-"));
  t.after(() => fsp.rm(root, { recursive: true, force: true }));
  const previousLocal = process.env.LOCALAPPDATA;
  process.env.LOCALAPPDATA = path.join(root, "local");
  t.after(() => { process.env.LOCALAPPDATA = previousLocal; });
  const state = { staging: path.join(root, "staging"), persistent: { mods: { nivalisnights: {} } } };
  await fsp.mkdir(state.staging, { recursive: true });
  const actionLog = [];
  const vortexApi = {
    actions: {
      setModType: (...args) => ({ type: "type", args })
    },
    selectors: { installPathForGame: () => state.staging },
    util: { GameStoreHelper: {} }
  };
  const originalLoad = Module._load;
  Module._load = function(request, parent, isMain) { return request === "vortex-api" ? vortexApi : originalLoad.call(this, request, parent, isMain); };
  delete require.cache[require.resolve("../src/index")];
  const helpers = require("../src/index")._test;
  Module._load = originalLoad;
  const notifications = [];
  let loggedIn = false, downloadUrl = "", automaticInstallNotification;
  const api = {
    store: { dispatch(action) { actionLog.push(action); } }, getState: () => state,
    sendNotification(value) { notifications.push(value); }, dismissNotification() { dismissed++; },
    ext: {
      async ensureLoggedIn() { loggedIn = true; },
      async nexusGetModFiles(gameId, modId) {
        assert.equal(gameId, "nivalisnights"); assert.equal(modId, 81);
        return [
          { category_id: 2, file_id: 10, file_name: "LUZ-Civic-Terminal-9.0.0-win-x64.zip", version: "9.0.0", uploaded_timestamp: 3 },
          { category_id: 1, file_id: 11, file_name: "LUZ-Civic-Terminal-0.8.1-win-x64.zip", version: "0.8.1", uploaded_timestamp: 2 },
          { category_id: 1, file_id: 12, file_name: "LUZ-Civic-Terminal-0.8.5-win-x64.zip", version: "0.8.5", uploaded_timestamp: 1 }
        ];
      }
    },
    events: { emit(event, ...args) {
      const callback = args.find((value) => typeof value === "function");
      if (event === "start-download") { downloadUrl = args[0][0]; callback(null, "download-id"); }
      else if (event === "start-install-download") {
        const folder = path.join(state.staging, "installed-luz");
        fs.mkdirSync(folder, { recursive: true });
        fs.writeFileSync(path.join(folder, "LUZ Civic Terminal.exe"), "fixture");
        state.persistent.mods.nivalisnights["installed-luz"] = { id: "installed-luz", installationPath: "installed-luz", state: "installed", type: "nivalisnights-luz-tool" };
        automaticInstallNotification = helpers.configureInstalledLuz({ api }, "nivalisnights", "installed-luz");
        callback(null, "installed-luz");
      }
    } }
  };
  let dismissed = 0;
  assert.equal(await helpers.downloadLuz({ api }), path.join(state.staging, "installed-luz", "LUZ Civic Terminal.exe"));
  assert.equal(await automaticInstallNotification, false, "bootstrap install events do not trigger a second setup review");
  assert.equal(loggedIn, true);
  assert.equal(downloadUrl, "nxm://nivalisnights/mods/81/files/12");
  assert.deepEqual(actionLog.map((action) => action.type), ["type"]);
  assert.equal(notifications[0].message, "Installing LUZ Civic Terminal");
  assert.equal(dismissed, 1);
  api.ext.ensureLoggedIn = async () => { throw new Error("login cancelled"); };
  await assert.rejects(helpers.downloadLuz({ api }), /login cancelled/);
  assert.deepEqual(actionLog.map((action) => action.type), ["type"]);
  assert.equal(dismissed, 2);
  await assert.rejects(helpers.latestLuzRelease({ ext: {} }), /cannot query/);
});

test("a Nexus-installed LUZ package configures itself and becomes the primary tool", async (t) => {
  const root = await fsp.mkdtemp(path.join(os.tmpdir(), "luz-vortex-nexus-install-"));
  t.after(() => fsp.rm(root, { recursive: true, force: true }));
  const previousLocal = process.env.LOCALAPPDATA;
  process.env.LOCALAPPDATA = path.join(root, "local");
  t.after(() => { process.env.LOCALAPPDATA = previousLocal; });

  const state = { staging: path.join(root, "staging"), game: path.join(root, "game"), persistent: { mods: { nivalisnights: {} } } };
  const installed = path.join(state.staging, "luz-from-nexus");
  const executable = path.join(installed, "LUZ Civic Terminal.exe");
  const exchange = path.join(process.env.LOCALAPPDATA, "LUZVortexBridge");
  const vortexRoot = path.join(exchange, "vortex-game");
  await Promise.all([fsp.mkdir(installed, { recursive: true }), fsp.mkdir(state.game, { recursive: true })]);
  await Promise.all([fsp.writeFile(executable, "fixture; never execute"), fsp.writeFile(path.join(state.game, "Nivalis Nights.exe"), "fixture"), fsp.writeFile(path.join(state.game, "GameAssembly.dll"), "fixture")]);
  state.persistent.mods.nivalisnights["luz-from-nexus"] = { id: "luz-from-nexus", installationPath: "luz-from-nexus", state: "installed", type: "nivalisnights-luz-tool" };

  const dispatched = [], notifications = [], reportedErrors = [];
  const vortexApi = {
    actions: {
      setModType: (...args) => ({ type: "type", args }),
      addDiscoveredTool: (...args) => ({ type: "tool", args }),
      setPrimaryTool: (...args) => ({ type: "primary", args })
    },
    selectors: { installPathForGame: () => state.staging },
    util: { GameStoreHelper: { findByAppId: async () => ({ gamePath: state.game }) } }
  };
  const originalLoad = Module._load;
  Module._load = function(request, parent, isMain) { return request === "vortex-api" ? vortexApi : originalLoad.call(this, request, parent, isMain); };
  await fsp.writeFile(path.join(installed, "LUZ Vortex.exe"), "handoff fixture");
  let setupOutcome = "Applied", setupVersion = "0.8.5", connectionExecutable = executable;
  const originalExecFile = childProcess.execFile;
  childProcess.execFile = function(program, args, options, callback) {
    assert.equal(program, executable);
    const request = JSON.parse(fs.readFileSync(args[1], "utf8"));
    fs.mkdirSync(path.join(exchange, "setup-results"), { recursive: true });
    fs.mkdirSync(vortexRoot, { recursive: true });
    fs.writeFileSync(path.join(exchange, "setup-results", `${request.RequestId}.json`), JSON.stringify({ Outcome: setupOutcome, Message: setupOutcome === "Failed" ? "setup failed" : "connected" }));
    fs.writeFileSync(path.join(exchange, "connection.json"), JSON.stringify({ SnapshotSchemas: [1, 2], SetupComplete: true, HandoffExecutable: path.join(path.dirname(connectionExecutable), "LUZ Vortex.exe"), Schema: 1, ConnectionId: "b".repeat(32), GameId: "nivalisnights", GameFolder: state.game, ExchangeRoot: exchange, VortexRoot: vortexRoot, Executable: connectionExecutable, Version: setupVersion }));
    callback(null, "", "");
    return { kill() {} };
  };
  t.after(() => { Module._load = originalLoad; childProcess.execFile = originalExecFile; });
  delete require.cache[require.resolve("../src/index")];
  const extension = require("../src/index");
  Module._load = originalLoad;

  let didInstall;
  const context = {
    registerGame() {}, registerStartHook() {}, registerModType() {}, registerInstaller() {},
    once(callback) { callback(); },
    api: {
      getState: () => state,
      onAsync() {}, showDialog: async () => ({ action: "Connect LUZ" }),
      events: { on(event, callback) { if (event === "did-install-mod") didInstall = callback; } },
      store: { dispatch(action) { dispatched.push(action); } },
      sendNotification(value) { notifications.push(value); }, showErrorNotification(title, error) { reportedErrors.push({ title, error }); }, log() {}
    }
  };
  extension.init(context);
  assert.equal(await didInstall("nivalisnights", "download-id", "luz-from-nexus"), true);
  assert.deepEqual(dispatched.map((action) => action.type), ["type", "tool", "primary"]);
  assert.equal(notifications.at(-1).title, "LUZ connected");
  assert.equal(JSON.parse(await fsp.readFile(path.join(exchange, "connection.json"), "utf8")).Executable, executable);
  const alreadyRunning = path.join(root, "existing", "LUZ Civic Terminal.exe");
  await fsp.mkdir(path.dirname(alreadyRunning), { recursive: true });
  await fsp.writeFile(alreadyRunning, "existing LUZ process fixture");
  await fsp.writeFile(path.join(path.dirname(alreadyRunning), "LUZ Vortex.exe"), "handoff fixture");
  await fsp.rm(path.join(exchange, "connection.json"), { force: true });
  connectionExecutable = alreadyRunning;
  dispatched.length = 0; notifications.length = 0;
  assert.equal(await didInstall("nivalisnights", "download-id", "luz-from-nexus"), true);
  assert.equal(dispatched.some((action) => action.type === "primary"), true);
  assert.equal(notifications.some((item) => item.title === "LUZ connected"), true);
  assert.equal(await didInstall("other-game", "download-id", "luz-from-nexus"), false);
  state.persistent.mods.nivalisnights["luz-from-nexus"].type = "luz-bridge";
  assert.equal(await didInstall("nivalisnights", "download-id", "luz-from-nexus"), false);
  state.persistent.mods.nivalisnights["luz-from-nexus"].type = "nivalisnights-luz-tool";
  await fsp.rm(path.join(exchange, "connection.json"), { force: true });
  connectionExecutable = executable;
  setupOutcome = "Failed";
  dispatched.length = 0; notifications.length = 0; reportedErrors.length = 0;
  await didInstall("nivalisnights", "download-id", "luz-from-nexus");
  assert.equal(dispatched.some((action) => action.type === "primary"), false);
  assert.equal(notifications.some((item) => item.title === "LUZ connected"), false);
  assert.match(String(reportedErrors.at(-1)?.error), /setup failed/);
  await fsp.rm(path.join(exchange, "connection.json"), { force: true });
  setupOutcome = "Applied"; setupVersion = "0.8.1";
  dispatched.length = 0; notifications.length = 0; reportedErrors.length = 0;
  await didInstall("nivalisnights", "download-id", "luz-from-nexus");
  assert.equal(dispatched.some((action) => action.type === "primary"), false);
  assert.equal(notifications.some((item) => item.title === "LUZ connected"), false);
  assert.match(String(reportedErrors.at(-1)?.error), /requires 0\.8\.5/);
  await fsp.rm(path.join(exchange, "connection.json"), { force: true });
  await fsp.rm(path.join(installed, "LUZ Vortex.exe"));
  setupVersion = "0.8.5";
  dispatched.length = 0; notifications.length = 0; reportedErrors.length = 0;
  await didInstall("nivalisnights", "download-id", "luz-from-nexus");
  assert.deepEqual(dispatched.map(action => action.type), ["type"], "Missing handoff never becomes the primary tool");
  assert.equal(notifications.length, 0);
  assert.equal(reportedErrors.at(-1)?.error.code, "ENOENT");
});


test("enabled game-extension conflicts and pending installers stop the bridge", () => {
  const helpers = require("../src/index")._test;
  assert.throws(() => helpers.assertNoConflicts({app:{extensions:{"game-nivalisnights":{enabled:true}}}}), /Another Nivalis/);
  assert.doesNotThrow(() => helpers.assertNoConflicts({app:{extensions:{"game-nivalisnights":{enabled:false}, "game-nivalisnights-luz":{enabled:true}}}}));
  assert.throws(() => helpers.inventoryFingerprint({persistent:{mods:{nivalisnights:{one:{id:"one",state:"installing"}}}}}), /still installing/);
});


test("conflict detection uses enabled extension metadata when Vortex keys entries by generated ID", async t => {
  const root = await fsp.mkdtemp(path.join(os.tmpdir(), "luz-extension-state-"));
  t.after(() => fsp.rm(root, { recursive: true, force: true }));
  const old = path.join(root, "Nivalis Nights Vortex Extension downloaded name"), own = path.join(root, "game-nivalisnights-luz");
  await fsp.mkdir(old); await fsp.mkdir(own);
  await fsp.writeFile(path.join(old, "info.json"), JSON.stringify({ name: "Nivalis Nights", gameId: 10401, version: "0.3.4" }));
  await fsp.writeFile(path.join(own, "info.json"), JSON.stringify({ id: "game-nivalisnights-luz", gameId: 10401, version: "0.6.1" }));
  const extensions = { generatedOldId: { path: old, enabled: false }, generatedOwnId: { path: own, enabled: true } };
  const helpers = require("../src/index")._test;
  assert.doesNotThrow(() => helpers.assertNoConflicts({ app: { extensions } }));
  extensions.generatedOldId.enabled = true;
  assert.throws(() => helpers.assertNoConflicts({ app: { extensions } }), /Another Nivalis Nights/);
});
