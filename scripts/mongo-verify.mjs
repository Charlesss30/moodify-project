import fs from 'node:fs';
import {MongoClient} from 'mongodb';
import {spawn} from 'node:child_process';
import net from 'node:net';
const client=new MongoClient('mongodb://127.0.0.1:27017');
let backend;
try {
 await client.connect();const db=client.db('mood_recommendation'), schemas=JSON.parse(fs.readFileSync('backend/Data/mongo-schema.json','utf8'));
 const collections={};
 for(const [name,validator]of Object.entries(schemas))collections[name]={count:await db.collection(name).countDocuments(),invalid:await db.collection(name).countDocuments({$nor:[validator]}),indexes:(await db.collection(name).listIndexes().toArray()).length};
 const report={checkedAt:new Date().toISOString(),collections,needsAudioReview:await db.collection('music').countDocuments({needsReview:true})};
 if(Object.values(collections).some(x=>x.invalid))throw new Error('Invalid migrated documents');
 const port=await new Promise(resolve=>{const server=net.createServer();server.listen(0,'127.0.0.1',()=>{const port=server.address().port;server.close(()=>resolve(port));});});
 backend=spawn('dotnet',[process.env.MOODIFY_BACKEND_DLL||'backend/bin/Debug/net10.0/mood_recommendation.dll'],{cwd:process.cwd(),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:'http://127.0.0.1:'+port,ASPNETCORE_CONTENTROOT:process.cwd()+'/backend'},windowsHide:true,stdio:'ignore'});
 let ready=false;
 for(let i=0;i<50;i++){try{const res=await fetch('http://127.0.0.1:'+port+'/api/Genres',{signal:AbortSignal.timeout(1000)});if(res.ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,200));}
 if(!ready)throw new Error('Live backend startup failed');
 const apiCounts={}, externalMetadata={};
 for(const endpoint of ['Movies','Music','Genres','MoodMapping']){
  const res=await fetch('http://127.0.0.1:'+port+'/api/'+endpoint);if(!res.ok)throw new Error(endpoint+' API failed');
  const items=await res.json();apiCounts[endpoint]=items.length;
  if(endpoint==='Movies'||endpoint==='Music'){
   const refs=items.filter(x=>x.isExternalReference);
   externalMetadata[endpoint]={count:refs.length,available:refs.filter(x=>x.metadataStatus==='available').length,withImages:refs.filter(x=>/^https:\/\//.test(x.hinhAnh||'')).length};
  }
 }
 for(const [endpoint,name] of [['Movies','movies'],['Music','music'],['Genres','genres'],['MoodMapping','moods']]){const prefix=endpoint==='Movies'?'OMDB_':endpoint==='Music'?'SPOTIFY_':null;const refs=prefix?await db.collection('externalReferences').countDocuments({_id:{$regex:'^'+prefix}}):0;if(apiCounts[endpoint]!==collections[name].count+refs)throw new Error(endpoint+' count mismatch');}
 report.apiCounts=apiCounts;report.externalMetadata=externalMetadata;
 fs.writeFileSync('docs/MONGODB_VALIDATION.json',JSON.stringify(report,null,2)+'\n');
 console.log(JSON.stringify(report));
}finally{
 if(backend&&backend.exitCode===null){await new Promise(resolve=>{backend.once('exit',resolve);backend.kill();});}
 await client.close();
}
