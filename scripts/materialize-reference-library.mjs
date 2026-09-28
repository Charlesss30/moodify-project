import fs from 'node:fs';
import {MongoClient,BSON} from 'mongodb';
import {spawn} from 'node:child_process';
import net from 'node:net';
if(!process.argv.includes('--apply'))throw Error('Use --apply to materialize the seeded references.');
const seed=JSON.parse(fs.readFileSync('docs/REFERENCE_LIBRARY_SEED.json','utf8'));
const ids=[...seed.movies,...seed.songs].map(x=>x.id);
const client=new MongoClient('mongodb://127.0.0.1:27017');let backend;
const dec=n=>BSON.Decimal128.fromString(String(n));
try{
 await client.connect();const db=client.db('mood_recommendation');
 const refs=await db.collection('externalReferences').find({_id:{$in:ids}}).toArray();
 if(!refs.length){console.log('No seeded references remain to convert.');process.exitCode=0;}
 else {
 const backup='.local-backups/materialize-references-'+Date.now()+'.json';fs.mkdirSync('.local-backups',{recursive:true});
 const snapshot={};for(const name of ['externalReferences','contents','movies','music'])snapshot[name]=await db.collection(name).find({_id:{$in:ids}}).toArray();
 fs.writeFileSync(backup,BSON.EJSON.stringify(snapshot,{relaxed:false}));
 if(snapshot.contents.some(x=>refs.some(r=>r._id===x._id)))throw Error('Overlapping local records; refusing to overwrite.');
 const port=await new Promise(resolve=>{const s=net.createServer();s.listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>resolve(p));});});
 backend=spawn('dotnet',['backend/bin/Debug/net10.0/mood_recommendation.dll'],{env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:'http://127.0.0.1:'+port,ASPNETCORE_CONTENTROOT:process.cwd()+'/backend'},windowsHide:true,stdio:'ignore'});
 const base='http://127.0.0.1:'+port+'/api/';let ready=false;
 for(let i=0;i<60;i++){try{if((await fetch(base+'Genres',{signal:AbortSignal.timeout(1000)})).ok){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,200));}
 if(!ready)throw Error('Backend startup failed.');
 const all=[];for(const endpoint of ['Movies','Music']){const r=await fetch(base+endpoint);if(!r.ok)throw Error('Metadata fetch failed');all.push(...await r.json());}
 const genres=await db.collection('genres').find().toArray();const genreByName=n=>genres.find(g=>g.name.toLowerCase()===n.toLowerCase());
 const groupNames={'r-n-b':'R&B','hip-hop':'Hip-Hop',edm:'Electronic'};
 const planned=[];
 for(const ref of refs){
 const item=all.find(x=>x.noiDungID===ref._id&&x.isExternalReference);
 if(!item||item.metadataStatus!=='available'||!item.hinhAnh?.startsWith('https://'))throw Error('Missing metadata/image: '+ref._id);
 const movie=ref._id.startsWith('OMDB_');let detail, valence=item.valence,arousal=item.arousal,rule=item.moodRuleVersion;
 if(movie){
  if(!item.moodAvailable)throw Error('Missing movie mood: '+ref._id);
  const gs=item.phim.theLoai.split(',').map(x=>genreByName(x.trim()));if(gs.some(x=>!x))throw Error('Unmapped movie genre: '+ref._id);
  detail={_id:ref._id,genreIds:gs.map(x=>x._id),averageRating:item.phim.diemDanhGiaTB==null?null:dec(item.phim.diemDanhGiaTB),imdbId:item.phim.imdbid||item.phim.imdbID||ref._id.slice(5),tmdbId:null,moodSource:'genre-default'};
 }else{
  const audit=seed.songs.find(x=>x.id===ref._id);const group=audit.selectionQuery.replace('genre:','');
  const g=genreByName(groupNames[group]||group);if(!g||g.defaultValence==null||g.defaultArousal==null)throw Error('Missing music defaults: '+group);
  valence=Number(g.defaultValence);arousal=Number(g.defaultArousal);rule='seed-category-default-v1';const t=item.nhac;
  detail={_id:ref._id,artist:t.tenNgheSi,album:t.album,genreId:g._id,duration:t.duration,source:'Spotify',externalId:t.externalId,sourceUrl:t.sourceUrl,releaseDate:t.releaseDate,lastSyncedAt:new Date(),moodSource:'seed-category-default'};
 }
 const content={_id:ref._id,title:item.tieuDe,contentType:movie?'Movie':'Song',imageUrl:item.hinhAnh,description:item.moTa||(item.nhac?.album?'Album: '+item.nhac.album:null),valence:dec(valence),arousal:dec(arousal),deleted:false,reviewStatus:'pending',moodRuleVersion:rule,modelVersion:null};
 planned.push({content,detail,collection:movie?'movies':'music'});
 }
 // Prepare every provider response before changing data. Preserve IDs and related ratings.
 const completed=[];
 for(const p of planned){let childInserted=false,parentInserted=false;
  try{
   await db.collection(p.collection).insertOne(p.detail);childInserted=true;
   await db.collection('contents').insertOne(p.content);parentInserted=true;
   const result=await db.collection('externalReferences').deleteOne({_id:p.content._id});
   if(result.deletedCount!==1)throw Error('Reference changed during conversion');
   completed.push(p.content._id);
  }catch(e){
   if(parentInserted)await db.collection('contents').deleteOne({_id:p.content._id});
   if(childInserted)await db.collection(p.collection).deleteOne({_id:p.content._id});
   throw e;
  }
 }
 const report={completedAt:new Date().toISOString(),backup,converted:completed.length,movies:planned.filter(p=>p.collection==='movies').length,songs:planned.filter(p=>p.collection==='music').length,musicMoodNote:'Draft estimates from seed search categories and configured genre defaults, not Spotify audio analysis or verified track genres.',ids:completed};
 fs.writeFileSync('docs/REFERENCE_LIBRARY_CONVERSION.json',JSON.stringify(report,null,2)+'\n');
 for(const endpoint of ['Movies','Music']){const r=await fetch(base+endpoint);const items=await r.json();const converted=items.filter(x=>ids.includes(x.noiDungID));if(converted.length!==50||converted.some(x=>x.isExternalReference||!x.hinhAnh||!x.moodAvailable))throw Error('Conversion verification failed: '+endpoint);console.log(endpoint+': 50 local editable records with images and mood');}
 console.log(JSON.stringify({converted:completed.length,backup}));
 }
}finally{if(backend&&backend.exitCode===null)await new Promise(r=>{backend.once('exit',r);backend.kill();});await client.close();}
