import fs from 'node:fs';
import {randomUUID} from 'node:crypto';
import {MongoClient,BSON} from 'mongodb';
if(!process.argv.includes('--apply'))throw Error('Stop the backend, then run with --apply.');
const client=new MongoClient(process.env.MONGO_URI||'mongodb://127.0.0.1:27017',{promoteValues:false});
try{
 await client.connect();const db=client.db(process.env.MONGO_DATABASE||'mood_recommendation');
 const snapshot={};for(const name of ['genres','contents'])snapshot[name]={documents:await db.collection(name).find().toArray(),indexes:await db.collection(name).listIndexes().toArray(),options:(await db.listCollections({name}).next()).options};
 fs.mkdirSync('.local-backups',{recursive:true});const backup='.local-backups/mongo-before-genre-defaults-'+Date.now()+'.json';fs.writeFileSync(backup,BSON.EJSON.stringify(snapshot,{relaxed:false}));
 const presets=JSON.parse(fs.readFileSync('scripts/genre-mood-presets.json','utf8'));const schemas=JSON.parse(fs.readFileSync('backend/Data/mongo-schema.json','utf8'));
 for(const preset of presets){
  const nameNormalized=preset.name.toUpperCase();const existing=await db.collection('genres').findOne({nameNormalized});
  // Explicitly cleared or customized configurations are never reset on rerun.
  if(existing && ('defaultValence' in existing || 'defaultArousal' in existing))continue;
  const values={...preset,moodVersion:new BSON.Int32(1)};
  if(existing)await db.collection('genres').updateOne({_id:existing._id},{$set:values});
  else await db.collection('genres').insertOne({_id:'GENRE_'+randomUUID().replaceAll('-',''),...values,nameNormalized});
 }
 await db.collection('contents').updateMany({reviewStatus:{$exists:false}},{$set:{reviewStatus:'pending',moodRuleVersion:null,modelVersion:null}});
 for(const name of ['genres','contents']){
  if(await db.collection(name).countDocuments({$nor:[schemas[name]]}))throw Error('Invalid '+name+' documents; backup: '+backup);
  await db.command({collMod:name,validator:schemas[name],validationLevel:'strict',validationAction:'error'});
 }
 console.log(JSON.stringify({backup,genres:await db.collection('genres').countDocuments(),contents:await db.collection('contents').countDocuments(),contentCoordinatesUnchanged:true}));
}finally{await client.close();}
