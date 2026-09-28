import fs from 'node:fs';
import {MongoClient, BSON} from 'mongodb';
import {collections,fields,roles} from './english-schema.mjs';

if(!process.argv.includes('--apply'))throw Error('Usage: node scripts/mongo-english.mjs --apply (stop the backend first)');
const client=new MongoClient(process.env.MONGO_URI||'mongodb://127.0.0.1:27017',{promoteValues:false});
const database=process.env.MONGO_DATABASE||'mood_recommendation';
const labels={'Vui':'Happy','Buon':'Sad','Thu gian':'Relaxed','Hao hung':'Excited','Tap trung':'Focused','Lang man':'Romantic','Hoai niem':'Nostalgic'};
const schemas=JSON.parse(fs.readFileSync('backend/Data/mongo-schema.json','utf8'));
const stringify=value=>BSON.EJSON.stringify(value,{relaxed:false});
function transform(doc,name){
 const result=Object.fromEntries(Object.entries(doc).map(([key,value])=>[fields[key]||key,value]));
 if(name==='TaiKhoan')result.role=roles[result.role]||result.role;
 if(name==='TamTrang'){result.name=labels[result.name]||result.name;result.nameNormalized=result.name.trim().toUpperCase();}
 if(name==='_Counters')result._id=collections[result._id]||result._id;
 if(result.legacyAudioFeatures)result.legacyAudioFeatures=Object.fromEntries(Object.entries(result.legacyAudioFeatures).map(([k,v])=>[fields[k]||k,v]));
 return result;
}
try{
 await client.connect();const db=client.db(database);const names=(await db.listCollections().toArray()).map(x=>x.name);
 if(Object.values(collections).every(n=>names.includes(n))&&!Object.keys(collections).some(n=>names.includes(n))){console.log('English schema already applied.');process.exitCode=0;}
 else{
  if(Object.values(collections).some(n=>names.includes(n)))throw Error('Mixed schemas detected. Restore the backup before retrying.');
  if(Object.keys(collections).some(n=>!names.includes(n)))throw Error('Expected source collections are missing; no changes made.');
  const snapshot={database,createdAt:new Date().toISOString(),collections:{}};
  for(const name of Object.keys(collections))snapshot.collections[name]={documents:await db.collection(name).find().toArray(),indexes:await db.collection(name).listIndexes().toArray(),options:(await db.listCollections({name}).next()).options};
  fs.mkdirSync('.local-backups',{recursive:true});const backup='.local-backups/mongo-before-english-'+Date.now()+'.json';fs.writeFileSync(backup,stringify(snapshot));
  const created=[];
  try{
   for(const [oldName,newName]of Object.entries(collections)){
    await db.createCollection(newName,schemas[newName]?{validator:schemas[newName],validationLevel:'strict',validationAction:'error'}:{});created.push(newName);
    const expected=snapshot.collections[oldName].documents.map(doc=>transform(doc,oldName));
    if(expected.length)await db.collection(newName).insertMany(expected);
    for(const index of snapshot.collections[oldName].indexes){
     if(index.name==='_id_')continue;
     const key=Object.fromEntries(Object.entries(index.key).map(([k,v])=>[fields[k]||k,Number(v)]));const options={};
     if(index.unique)options.unique=true;
     if(index.partialFilterExpression)options.partialFilterExpression=Object.fromEntries(Object.entries(index.partialFilterExpression).map(([k,v])=>[fields[k]||k,v]));
     await db.collection(newName).createIndex(key,options);
    }
    const actual=await db.collection(newName).find().toArray();const byId=new Map(actual.map(doc=>[stringify(doc._id),stringify(doc)]));
    if(actual.length!==expected.length||expected.some(doc=>byId.get(stringify(doc._id))!==stringify(doc)))throw Error('Verification failed: '+newName);
    if(schemas[newName]&&await db.collection(newName).countDocuments({$nor:[schemas[newName]]}))throw Error('Invalid documents: '+newName);
   }
  }catch(error){for(const name of created)await db.collection(name).drop();throw error;}
  // All replacement collections and BSON values verified before removing originals.
  for(const name of Object.keys(collections))await db.collection(name).drop();
  const report={database,completedAt:new Date().toISOString(),backup,collections:Object.fromEntries(Object.entries(collections).map(([a,b])=>[b,{previousName:a,count:snapshot.collections[a].documents.length}]))};
  fs.writeFileSync('docs/MONGODB_ENGLISH_RESULT.json',JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report));
 }
}finally{await client.close();}
