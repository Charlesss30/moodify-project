import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {MongoClient,BSON} from 'mongodb';
const root=path.resolve(import.meta.dirname,'..');
const read=p=>fs.existsSync(p)?JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,'')):{};
const base=read(path.join(root,'backend/appsettings.json')),local=read(path.join(root,'backend/appsettings.Development.json'));
const config={...base,...local,Mongo:{...base.Mongo,...local.Mongo}};
const stringify=x=>BSON.EJSON.stringify(x,{relaxed:false});
const digest=x=>crypto.createHash('sha256').update(stringify(x)).digest('hex');
const client=new MongoClient(process.env.MONGO_URI||config.Mongo?.ConnectionString||'mongodb://127.0.0.1:27017');
const dbName=process.env.Mongo__Database||config.Mongo?.Database||'mood_recommendation';
const apply=process.argv.indexOf('--apply');
const movieFields=['tmdbId','imdbId','director','genreIds','averageRating','trailerUrl','metadataSource','ratingSource'];
const contentFields=['title','description','imageUrl','updatedAt'];
const protectedRow=(row,fields)=>Object.fromEntries(Object.entries(row).filter(([k])=>!fields.includes(k)));
try {
 await client.connect();const db=client.db(dbName);
 if(apply<0){
  const token=process.env.Tmdb__AccessToken||local.Tmdb?.AccessToken||base.Tmdb?.AccessToken;
  if(!token)throw Error('TMDB access token is not configured.');
  const snapshot={};
  for(const name of ['movies','contents','genres','music','moods','ratings','moodHistory','interactions','externalReferences'])snapshot[name]=await db.collection(name).find({}).sort({_id:1}).toArray();
  const dir=path.join(root,'.local-backups','tmdb-migration-'+new Date().toISOString().replace(/[:.]/g,'-'));fs.mkdirSync(dir,{recursive:true});
  fs.writeFileSync(path.join(dir,'before.ejson'),stringify(snapshot));
  const plan={database:dbName,createdAt:new Date(),backup:'before.ejson',changes:[],failures:[],protectedHashes:{}};
  for(const name of ['genres','music','moods','ratings','moodHistory','interactions','externalReferences'])plan.protectedHashes[name]=digest(snapshot[name]);
  async function get(route){for(let attempt=0;attempt<3;attempt++){try{const r=await fetch('https://api.themoviedb.org/3/'+route,{headers:{Authorization:'Bearer '+token},signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error('TMDB HTTP '+r.status);return await r.json();}catch(e){if(attempt===2)throw e;await new Promise(r=>setTimeout(r,500*(attempt+1)));}}}
  let index=0;
  async function worker(){while(index<snapshot.movies.length){const movie=snapshot.movies[index++];try{
    const content=snapshot.contents.find(c=>c._id===movie._id);if(!content||content.contentType!=='Movie')throw Error('Missing content');
    if(!/^tt\d{7,10}$/.test(movie.imdbId||''))throw Error('Missing valid IMDb ID');
    const found=await get('find/'+movie.imdbId+'?external_source=imdb_id');
    if(found.movie_results?.length!==1)throw Error('No unique movie match');
    const id=found.movie_results[0].id;const m=await get('movie/'+id+'?language=en-US&append_to_response=credits,videos');
    if(m.id!==id||m.imdb_id!==movie.imdbId)throw Error('IMDb identity mismatch');
    if(!m.title||!/^\/[A-Za-z0-9_-]+\.(jpg|png|webp)$/.test(m.poster_path||''))throw Error('Missing title or poster');
    const names=m.genres.map(g=>g.name==='Science Fiction'?'Sci-Fi':g.name);
    const genreIds=names.map(n=>{const g=snapshot.genres.find(g=>g.nameNormalized===n.trim().toUpperCase()&&['Movie','Both'].includes(g.contentType));if(!g)throw Error('Missing configured genre: '+n);return g._id;});
    const trailer=m.videos?.results?.find(v=>v.site==='YouTube'&&v.type==='Trailer'&&v.official===true&&/^[A-Za-z0-9_-]{11}$/.test(v.key));
    plan.changes.push({id:movie._id,oldMovie:movie,oldContent:content,moviePatch:{tmdbId:String(id),imdbId:m.imdb_id,director:[...new Set((m.credits?.crew||[]).filter(x=>x.job==='Director').map(x=>x.name))].join(', '),genreIds,averageRating:m.vote_count>0?BSON.Decimal128.fromString(String(m.vote_average)):null,trailerUrl:trailer?'https://www.youtube.com/watch?v='+trailer.key:null,metadataSource:'TMDB',ratingSource:'TMDB'},contentPatch:{title:m.title,description:m.overview||null,imageUrl:'https://image.tmdb.org/t/p/w500'+m.poster_path,updatedAt:new Date()}});
   }catch(e){plan.failures.push({id:movie._id,reason:e.message});}}}
  await Promise.all([worker(),worker(),worker()]);plan.changes.sort((a,b)=>a.id.localeCompare(b.id));
  fs.writeFileSync(path.join(dir,'plan.ejson'),stringify(plan));
  console.log(JSON.stringify({plan:path.relative(root,path.join(dir,'plan.ejson')),database:dbName,total:snapshot.movies.length,ready:plan.changes.length,withTrailer:plan.changes.filter(c=>c.moviePatch.trailerUrl).length,failures:plan.failures}));
 }else{
  const file=path.resolve(process.argv[apply+1]||'');const allowed=path.join(root,'.local-backups')+path.sep;
  if(!file.startsWith(allowed)||!file.endsWith('plan.ejson'))throw Error('Use the generated local migration plan.');
  const plan=BSON.EJSON.parse(fs.readFileSync(file,'utf8'),{relaxed:false});
  if(plan.database!==dbName||plan.failures.length)throw Error('Plan database mismatch or unresolved failures.');
  const journal=path.join(path.dirname(file),'applied.json');if(fs.existsSync(journal))throw Error('Plan already started; inspect journal before retrying.');
  for(const row of plan.changes)for(const [name,old] of [['movies',row.oldMovie],['contents',row.oldContent]])if(digest(await db.collection(name).findOne({_id:row.id}))!==digest(old))throw Error('Data changed since plan: '+row.id);
  const completed=[];fs.writeFileSync(journal,JSON.stringify(completed));
  const cas=old=>({_id:old._id,$expr:{$eq:['$$ROOT',{$literal:old}]}});
  for(const row of plan.changes){
    const c=await db.collection('contents').updateOne(cas(row.oldContent),{$set:row.contentPatch});if(c.matchedCount!==1)throw Error('Concurrent content edit: '+row.id);
    const m=await db.collection('movies').updateOne(cas(row.oldMovie),{$set:row.moviePatch});if(m.matchedCount!==1){await db.collection('contents').replaceOne(cas({...row.oldContent,...row.contentPatch}),row.oldContent);throw Error('Concurrent movie edit: '+row.id);}
    completed.push(row.id);fs.writeFileSync(journal,JSON.stringify(completed));
  }
  for(const row of plan.changes){const m=await db.collection('movies').findOne({_id:row.id}),c=await db.collection('contents').findOne({_id:row.id});if(digest(protectedRow(m,movieFields))!==digest(protectedRow(row.oldMovie,movieFields))||digest(protectedRow(c,contentFields))!==digest(protectedRow(row.oldContent,contentFields)))throw Error('Protected fields changed: '+row.id);}
  const protectedChecks={};for(const [name,hash] of Object.entries(plan.protectedHashes))protectedChecks[name]=digest(await db.collection(name).find({}).sort({_id:1}).toArray())===hash;
  const result={database:dbName,updated:completed.length,totalTmdb:await db.collection('movies').countDocuments({metadataSource:'TMDB',ratingSource:'TMDB'}),protectedChecks,verifiedAt:new Date()};fs.writeFileSync(path.join(path.dirname(file),'result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
 }
}finally{await client.close();}
