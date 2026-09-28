import fs from 'node:fs';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {MongoClient,BSON} from 'mongodb';
const root=path.resolve(import.meta.dirname,'..');
const args=process.argv.slice(2),mode=args.shift();
function option(key){const i=args.indexOf(key);return i<0?undefined:args[i+1];}
const readConfig=file=>fs.existsSync(file)?JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,'')):{};
const config={...readConfig(path.join(root,'backend/appsettings.json')),...readConfig(path.join(root,'backend/appsettings.Development.json'))};
const uri=process.env.MONGO_URI||config.Mongo?.ConnectionString||'mongodb://127.0.0.1:27017';
const database=option('--database')||process.env.MONGO_DATABASE||config.Mongo?.Database||'mood_recommendation';
const encode=value=>BSON.EJSON.stringify(value,null,2,{relaxed:false})+'\n';
const decode=text=>BSON.EJSON.parse(text,{relaxed:false});
const hash=text=>createHash('sha256').update(text).digest('hex');
const safeName=name=>typeof name==='string'&&/^[A-Za-z_][A-Za-z0-9_-]*$/.test(name);
if(!['export','import'].includes(mode)){
 console.log('Export: node scripts/mongo-backup.mjs export [--out .local-backups/my-backup] [--database name]\nImport: node scripts/mongo-backup.mjs import --from .local-backups/my-backup --database new_empty_database --apply');
 process.exit(mode?1:0);
}
if(['admin','local','config'].includes(database)||!safeName(database))throw Error('Choose an application database name.');
const client=new MongoClient(uri,{serverSelectionTimeoutMS:5000,promoteValues:false});
try{
 await client.connect();const db=client.db(database);
 if(mode==='export'){
  const infos=await db.listCollections().toArray();
  if(!infos.length)throw Error('Source database is empty or does not exist.');
  if(infos.some(x=>x.type!=='collection'||!safeName(x.name)||x.options?.timeseries||x.options?.capped))throw Error('This dataset exporter supports ordinary collections only. Use MongoDB Database Tools for views/capped/time-series collections.');
  const out=path.resolve(root,option('--out')||'.local-backups/mongo-'+database+'-'+new Date().toISOString().replaceAll(':','-'));
  if(fs.existsSync(out))throw Error('Output folder already exists; choose a new folder.');
  fs.mkdirSync(out,{recursive:true});const collections=[];
  for(const info of infos.sort((a,b)=>a.name.localeCompare(b.name))){
   const collection=db.collection(info.name),documents=await collection.find().toArray();
   const text=encode(documents),file=info.name+'.json';fs.writeFileSync(path.join(out,file),text,{flag:'wx'});
   collections.push({name:info.name,file,count:documents.length,sha256:hash(text),options:info.options,indexes:await collection.listIndexes().toArray()});
  }
  // Write the manifest last: incomplete exports cannot be imported.
  const manifest={format:'moodify-mongo-dataset',version:1,database,exportedAt:new Date().toISOString(),collections};
  fs.writeFileSync(path.join(out,'manifest.json'),encode(manifest),{flag:'wx'});
  console.log(JSON.stringify({folder:out,collections:collections.length,documents:collections.reduce((n,c)=>n+c.count,0)}));
 }else{
  if(!option('--from')||!option('--database'))throw Error('Import requires --from and an explicit --database.');
  const folder=path.resolve(root,option('--from')),manifest=decode(fs.readFileSync(path.join(folder,'manifest.json'),'utf8'));
  if(manifest.format!=='moodify-mongo-dataset'||Number(manifest.version)!==1||!Array.isArray(manifest.collections)||!manifest.collections.length)throw Error('Invalid backup manifest.');
  const seen=new Set(),prepared=[];
  for(const entry of manifest.collections){
   if(!safeName(entry.name)||entry.file!==entry.name+'.json'||seen.has(entry.name)||!Array.isArray(entry.indexes))throw Error('Invalid/duplicate collection entry.');seen.add(entry.name);
   const text=fs.readFileSync(path.join(folder,entry.file),'utf8');
   if(hash(text)!==entry.sha256)throw Error('Checksum mismatch: '+entry.file);
   const documents=decode(text);
   if(!Array.isArray(documents)||documents.length!==Number(entry.count)||documents.some(x=>!x||x._id===undefined))throw Error('Invalid document data: '+entry.file);
   prepared.push({...entry,documents});
  }
  if(await db.listCollections().hasNext())throw Error('Target database must be empty. Existing data will not be overwritten.');
  if(!args.includes('--apply')){console.log('Validated '+prepared.length+' collections. Add --apply to restore into '+database+'.');}
  else {
   // Restore typed options/validators. Ordinary command option numbers remain JS numbers.
   for(const entry of prepared){
    const options=BSON.EJSON.parse(BSON.EJSON.stringify(entry.options||{}));
    const col=await db.createCollection(entry.name,options);
    for(let offset=0;offset<entry.documents.length;offset+=500)await col.insertMany(entry.documents.slice(offset,offset+500));
    for(const index of entry.indexes){
     if(index.name==='_id_')continue;
     const {key,v,ns,...indexOptions}=BSON.EJSON.parse(BSON.EJSON.stringify(index));
     await col.createIndex(key,indexOptions);
    }
    if(Number(await col.countDocuments())!==entry.documents.length)throw Error('Restore count mismatch: '+entry.name);
   }
   console.log(JSON.stringify({database,restoredCollections:prepared.length,documents:prepared.reduce((n,e)=>n+e.documents.length,0)}));
  }
 }
}catch(error){console.error(error.message);process.exitCode=1;}finally{await client.close();}
