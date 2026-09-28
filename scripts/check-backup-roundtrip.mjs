import fs from 'node:fs';
import {spawnSync} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import assert from 'node:assert/strict';
import {MongoClient,BSON} from 'mongodb';
const folder=process.argv[2];if(!folder)throw Error('Pass the backup folder to verify.');
const database='moodify_backup_test_'+randomUUID().replaceAll('-','');
const read=p=>fs.existsSync(p)?JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,'')):{};
const config={...read('backend/appsettings.json'),...read('backend/appsettings.Development.json')};
const client=new MongoClient(process.env.MONGO_URI||config.Mongo?.ConnectionString||'mongodb://127.0.0.1:27017',{promoteValues:false,serverSelectionTimeoutMS:5000});
try{
 await client.connect();const db=client.db(database);assert.equal(await db.listCollections().hasNext(),false);
 const run=spawnSync(process.execPath,['scripts/mongo-backup.mjs','import','--from',folder,'--database',database,'--apply'],{encoding:'utf8',windowsHide:true});
 if(run.status!==0)throw Error(run.stderr||run.stdout);
 const manifest=BSON.EJSON.parse(fs.readFileSync(folder+'/manifest.json','utf8'),{relaxed:false});let total=0;
 for(const entry of manifest.collections){
  const expected=BSON.EJSON.parse(fs.readFileSync(folder+'/'+entry.file,'utf8'),{relaxed:false});
  const actual=await db.collection(entry.name).find().toArray();
  const canonical=docs=>docs.map(x=>BSON.EJSON.stringify(x,{relaxed:false})).sort();
  assert.deepEqual(canonical(actual),canonical(expected),entry.name+' documents/types');
  const indexes=await db.collection(entry.name).listIndexes().toArray();assert.deepEqual(canonical(indexes),canonical(entry.indexes),entry.name+' indexes');
  const info=await db.listCollections({name:entry.name}).next();assert.deepEqual(info.options,entry.options,entry.name+' schema');total+=actual.length;
 }
 const repeat=spawnSync(process.execPath,['scripts/mongo-backup.mjs','import','--from',folder,'--database',database,'--apply'],{encoding:'utf8',windowsHide:true});assert.notEqual(repeat.status,0);assert.match(repeat.stderr,/must be empty/);
 console.log('PASS: '+manifest.collections.length+' collections, '+total+' documents; BSON types, validators, indexes preserved; existing database protected.');
}finally{await client.db(database).dropDatabase();await client.close();}
