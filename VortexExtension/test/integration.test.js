const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { buildSnapshot } = require('../src/snapshot');

test('inventory mutation prevents publication and preserves the last complete snapshot', async t => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'luz-inventory-'));
  t.after(() => fs.rm(root, {recursive:true, force:true}));
  const staging = path.join(root,'staging'), exchange = path.join(root,'exchange');
  await fs.mkdir(path.join(staging,'mod','Wrap','plugins','Foo'), {recursive:true});
  await fs.writeFile(path.join(staging,'mod','Wrap','plugins','Foo','Foo.dll'), 'selected plugin');
  await fs.mkdir(path.join(staging,'mod','Wrap','config'), {recursive:true});
  await fs.writeFile(path.join(staging,'mod','Wrap','config','Foo.cfg'), 'selected config');
  const descriptor = {ExchangeRoot:exchange, ConnectionId:'a'.repeat(32), GameFolder:path.join(root,'game')};
  const mods = [{id:'one', state:'installed', installationPath:'mod', attributes:{luzBridgeId:'persistent-one', version:'1.0.0'}}];
  const first = await buildSnapshot({descriptor, stagingRoot:staging, mods});
  assert.equal(first.manifest.Schema,2);
  assert.equal(first.manifest.Packages[0].BridgeId,'persistent-one');
  assert.ok(first.manifest.Packages[0].Files.some(f=>f.Path==='Wrap/config/Foo.cfg'));
  await assert.rejects(buildSnapshot({descriptor, stagingRoot:staging, mods, verifyInventory(){throw new Error('inventory changed');}}), /inventory changed/);
  assert.deepEqual(JSON.parse(await fs.readFile(first.manifestPath,'utf8')), first.manifest);
  await assert.rejects(buildSnapshot({descriptor, stagingRoot:staging, mods, requestId:first.manifest.RequestId}), /already exists/);
  assert.deepEqual(JSON.parse(await fs.readFile(first.manifestPath,'utf8')), first.manifest);
});

test('linked package root cannot escape staging', async t => {
  const root=await fs.mkdtemp(path.join(os.tmpdir(),'luz-staging-link-'));
  t.after(()=>fs.rm(root,{recursive:true,force:true}));
  await fs.mkdir(path.join(root,'staging')); await fs.mkdir(path.join(root,'outside'));
  await fs.writeFile(path.join(root,'outside','payload.dll'),'outside');
  await fs.symlink(path.join(root,'outside'),path.join(root,'staging','mod'),process.platform==='win32'?'junction':'dir');
  await assert.rejects(buildSnapshot({descriptor:{ExchangeRoot:path.join(root,'exchange')},stagingRoot:path.join(root,'staging'),mods:[{id:'linked',installationPath:'mod'}]}),/Linked|Links/);
});
