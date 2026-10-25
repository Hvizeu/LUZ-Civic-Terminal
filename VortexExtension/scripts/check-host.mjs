import { spawnSync } from "node:child_process";

if (!process.env.LUZ_VORTEX_ASAR) {
  console.error("Set LUZ_VORTEX_ASAR to the installed Vortex 2.9.0 resources/app.asar. No host checks were run.");
  process.exit(1);
}
const result = spawnSync(process.execPath, ["--test", "test/host.test.js"], { stdio: "inherit" });
process.exit(result.status ?? 1);
