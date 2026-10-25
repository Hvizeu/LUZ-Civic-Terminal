import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const output = path.join(root, "dist");
await fs.rm(output, { recursive: true, force: true });
await fs.mkdir(output, { recursive: true });
for (const file of ["index.js", "snapshot.js"])
  await fs.copyFile(path.join(root, "src", file), path.join(output, file));
for (const file of ["info.json", "README.md", "LICENSE", "THIRD-PARTY-NOTICES.txt", "gameart.png"])
  await fs.copyFile(path.join(root, file), path.join(output, file));
