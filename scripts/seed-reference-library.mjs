import fs from 'node:fs';
import {MongoClient,BSON} from 'mongodb';
if(!process.argv.includes('--apply'))throw Error('Use --apply to verify provider metadata and add 50 movie IDs + 50 music IDs.');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const cfg={...read('backend/appsettings.json'),...read('backend/appsettings.Development.json')};
const client=new MongoClient(process.env.MONGO_URI||cfg.Mongo.ConnectionString);
const movieIds=['tt0109830','tt0111161','tt0120689','tt1675434','tt0119217','tt0405469','tt4468740','tt0211915','tt2883512','tt0359950','tt2194499','tt0112471','tt0414387','tt0332280','tt0338013','tt1798709','tt2543164','tt0816692','tt1375666','tt0133093','tt1856101','tt0470752','tt3659388','tt1454468','tt1182345','tt0120737','tt0241527','tt0245429','tt0096283','tt0347149','tt2380307','tt2096673','tt2948372','tt0910970','tt1049413','tt0266543','tt2278388','tt2582802','tt4846340','tt2084970','tt8946378','tt0114369','tt5052448','tt6644200','tt1457767','tt0081505','tt6751668','tt5700672','tt8579674','tt0118799','tt0372784','tt0088763','tt0110357','tt0112573','tt0361748'];
// Search categories diversify the selection; they are not treated as verified per-track genre labels.
const searches=['genre:pop','genre:r-n-b','genre:hip-hop','genre:rock','genre:edm','genre:jazz','genre:blues','genre:classical','genre:country','genre:folk','genre:acoustic','genre:soul','genre:latin','chill','ballad'];
async function json(url,options={}){
 const response=await fetch(url,{...options,signal:AbortSignal.timeout(25000)});
 if(!response.ok)throw Error('Provider HTTP '+response.status);
 return response.json();
}
async function imageAvailable(url){
 if(!url?.startsWith('https://'))return false;
 try {const r=await fetch(url,{method:'HEAD',signal:AbortSignal.timeout(12000)});return r.ok&&r.headers.get('content-type')?.startsWith('image/');}catch{return false;}
}
try{
 await client.connect();const db=client.db(process.env.MONGO_DATABASE||cfg.Mongo.Database);
 const schemas=read('backend/Data/mongo-schema.json');
 if(!await db.listCollections({name:'externalReferences'}).hasNext())await db.createCollection('externalReferences',{validator:schemas.externalReferences});
 const refs=db.collection('externalReferences');
 const existingMovies=new Set((await db.collection('movies').find({},{projection:{imdbId:1}}).toArray()).map(x=>x.imdbId));
 const existingMusic=new Set((await db.collection('music').find({source:'Spotify'},{projection:{externalId:1}}).toArray()).map(x=>x.externalId));
 const before=await refs.find().toArray();fs.mkdirSync('.local-backups',{recursive:true});const backup='.local-backups/references-before-seed-'+Date.now()+'.json';fs.writeFileSync(backup,BSON.EJSON.stringify({database:db.databaseName,documents:before},{relaxed:false}));
 const movies=[],songs=[],seenSongs=new Set();
 for(const id of movieIds){
  if(movies.length===50)break;
  if(existingMovies.has(id))continue;
  const d=await json('https://www.omdbapi.com/?'+new URLSearchParams({apikey:cfg.Omdb.ApiKey,i:id,plot:'short',type:'movie'}));
  if(d.Response!=='True'||d.Type!=='movie'||d.imdbID!==id||!await imageAvailable(d.Poster))continue;
  movies.push({id:'OMDB_'+id,title:d.Title,genre:d.Genre});
  if(movies.length%10===0)console.log('Verified movie posters:',movies.length);
 }
 const token=await json('https://accounts.spotify.com/api/token',{method:'POST',headers:{Authorization:'Basic '+Buffer.from(cfg.Spotify.ClientId+':'+cfg.Spotify.ClientSecret).toString('base64'),'Content-Type':'application/x-www-form-urlencoded'},body:'grant_type=client_credentials'});
 const headers={Authorization:'Bearer '+token.access_token};
 for(let round=0;round<3&&songs.length<50;round++)for(const query of searches){
  if(songs.length===50)break;
  const data=await json('https://api.spotify.com/v1/search?'+new URLSearchParams({q:query,type:'track',limit:'10',offset:String(round*10),market:cfg.Spotify.Market||'VN'}),{headers});
  let added=0;
  for(const track of data.tracks?.items||[]){
   if(added===4||songs.length===50)break;
   if(!/^[a-zA-Z0-9]{22}$/.test(track.id)||existingMusic.has(track.id)||seenSongs.has(track.id))continue;
   const d=await json('https://api.spotify.com/v1/tracks/'+track.id+'?market=VN',{headers});
   if(!/^[a-zA-Z0-9]{22}$/.test(d.id)||existingMusic.has(d.id)||seenSongs.has(d.id)||!await imageAvailable(d.album?.images?.[0]?.url))continue;
   seenSongs.add(d.id);songs.push({id:'SPOTIFY_'+d.id,title:d.name,artist:d.artists.map(a=>a.name).join(', '),selectionQuery:query});added++;
  }
  console.log('Verified music covers:',songs.length);
 }
 if(movies.length!==50||songs.length!==50)throw Error('Need exactly 50 verified movies and songs before writing. Got '+movies.length+'/'+songs.length);
 const entries=[...movies,...songs];
 await refs.bulkWrite(entries.map(x=>({updateOne:{filter:{_id:x.id},update:{$setOnInsert:{_id:x.id}},upsert:true}})));
 const stored=await refs.find({_id:{$in:entries.map(x=>x.id)}}).toArray();
 if(stored.length!==100||stored.some(x=>Object.keys(x).length!==1))throw Error('ID-only verification failed');
 const bytes=stored.reduce((n,x)=>n+BSON.calculateObjectSize(x),0);
 fs.writeFileSync('docs/REFERENCE_LIBRARY_SEED.json',JSON.stringify({completedAt:new Date().toISOString(),backup,movieCount:movies.length,musicCount:songs.length,documentBytes:bytes,metadataStoredInDatabase:false,selectionNotes:'Music queries are selection categories, not verified track genres. Titles below are an audit report only.',movies,songs},null,2)+'\n');
 console.log(JSON.stringify({movies:50,songs:50,documentBytes:bytes,backup}));
}catch(error){console.error(error.message);process.exitCode=1;}finally{await client.close();}
