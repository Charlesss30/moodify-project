import fs from 'node:fs';
import {randomUUID} from 'node:crypto';
import {MongoClient,BSON} from 'mongodb';
if(!process.argv.includes('--apply'))throw Error('Use --apply to back up genres and add missing music groups.');
const client=new MongoClient(process.env.MONGO_URI||'mongodb://127.0.0.1:27017',{promoteValues:false});
try {
 await client.connect();
 const db=client.db(process.env.MONGO_DATABASE||'mood_recommendation');
 const info=await db.listCollections({name:'genres'}).next();
 if(!info)throw Error('English genres collection is required.');
 const genres=db.collection('genres');
 const backup={database:db.databaseName,documents:await genres.find().toArray(),indexes:await genres.listIndexes().toArray(),options:info.options};
 fs.mkdirSync('.local-backups',{recursive:true});
 const path='.local-backups/mongo-before-music-groups-'+Date.now()+'.json';
 fs.writeFileSync(path,BSON.EJSON.stringify(backup,{relaxed:false}));
 const groups=JSON.parse(fs.readFileSync('scripts/genre-mood-presets.json','utf8')).filter(g=>g.contentType==='Song');
 let added=0;
 for(const group of groups){
  const nameNormalized=group.name.toUpperCase();
  const result=await genres.updateOne({nameNormalized},{$setOnInsert:{_id:'GENRE_'+randomUUID().replaceAll('-',''),...group,nameNormalized,moodVersion:new BSON.Int32(1)}},{upsert:true});
  added+=result.upsertedCount;
 }
 console.log(JSON.stringify({backup:path,added,groups:groups.map(g=>g.name)}));
}finally{await client.close();}
