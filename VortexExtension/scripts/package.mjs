import fs from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const metadata = JSON.parse(await fs.readFile(path.join(root, "package.json"), "utf8"));
const release = path.join(root, "release");
const archive = path.join(release, `game-nivalisnights-${metadata.version}.zip`);
await fs.rm(release, { recursive: true, force: true });
await fs.mkdir(release, { recursive: true });
const command = `Compress-Archive -Path '${path.join(root, "dist", "*").replace(/'/g, "''")}' -DestinationPath '${archive.replace(/'/g, "''")}' -CompressionLevel Optimal -Force`;
const result = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command], { stdio: "inherit" });
if (result.status !== 0) process.exit(result.status ?? 1);
