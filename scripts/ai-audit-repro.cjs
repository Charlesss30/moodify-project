// Read-only source snapshot; all mutations are confined to a generated audit DB.
const {MongoClient}=require('./node_modules/mongodb');
const {spawn}=require('node:child_process'),net=require('node:net'),crypto=require('node:crypto'),fs=require('node:fs');
(async()=>{const c=new MongoClient('mongodb://127.0.0.1:27017');let child;const dbName='moodify_audit_'+crypto.randomUUID().replaceAll('-','');const findings=[];
try{await c.connect();const d=c.db(dbName);for(const name of ['moods','contents','movies','music','genres']){const rows=await c.db('mood_recommendation').collection(name).find({}).toArray();if(rows.length)await d.collection(name).insertMany(rows);}
const port=await new Promise(r=>{const s=net.createServer().listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>r(p))})});
child=spawn('dotnet',['D:/moodify-project/backend/bin/Debug/net10.0/mood_recommendation.dll'],{cwd:'D:/moodify-project/backend',windowsHide:true,stdio:'ignore',env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:`http://127.0.0.1:${port}`,Mongo__ConnectionString:'mongodb://127.0.0.1:27017',Mongo__Database:dbName,Jwt__Key:crypto.randomBytes(48).toString('base64'),AI__Enabled:'true',AI__ServiceUrl:'http://127.0.0.1:8001/',Spotify__ClientId:'',Spotify__ClientSecret:'',Tmdb__AccessToken:''}});
const base=`http://127.0.0.1:${port}/api`;async function call(path,body,token){const r=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(20000)});const text=await r.text();let data;try{data=JSON.parse(text)}catch{data=text}return {status:r.status,data};}function ok(r){if(r.status!==200)throw Error('Unexpected status '+r.status+': '+JSON.stringify(r.data).slice(0,150));return r.data;}
for(let i=0;i<80;i++){try{if((await call('/Genres')).status===200)break;}catch{}await new Promise(r=>setTimeout(r,150));}
const pass=crypto.randomBytes(16).toString('hex')+'Aa1!';ok(await call('/Auth/register',{tenDangNhap:'audit_admin',email:'audit@test.invalid',matKhau:pass}));await d.collection('accounts').updateOne({email:'audit@test.invalid'},{$set:{role:'Admin'}});
const token=ok(await call('/Auth/login',{identifier:'audit@test.invalid',matKhau:pass})).token;
ok(await call('/Models/roberta_large_asl_en_01/validate',{},token));ok(await call('/Models/roberta_large_asl_en_01/activate',{expectedRevision:0},token));
async function analyze(){return ok(await call('/MoodAnalysis',{text:'I feel happy and joyful today.',language:'en'},token))}
const a=await analyze();if(!a.suggestedMoodId)throw Error('No suggested mood from actual model');
const r1=ok(await call('/Recommendation',{tamTrangID:a.suggestedMoodId,source:'manual',analysisId:a.analysisId,loaiNoiDung:'Movie',limit:1},token));
const r2=await call('/Recommendation',{tamTrangID:a.suggestedMoodId,source:'manual',analysisId:a.analysisId,loaiNoiDung:'Music',limit:3},token);
findings.push({case:'Replay same analysis with different content filter/limit',status:r2.status,firstTypes:r1.items.map(x=>x.content.loaiNoiDung),secondTypes:r2.data.items?.map(x=>x.content.loaiNoiDung),secondCount:r2.data.items?.length,expected:'409 or recompute explicitly; never return Movie as Music request'});
const b=await analyze();await d.collection('moods').updateOne({_id:b.suggestedMoodId},{$set:{name:'Happy renamed for audit',nameNormalized:'HAPPY RENAMED FOR AUDIT'}});const renamed=await analyze();findings.push({case:'Rename mapped mood with same ID/bounds',before:b.suggestedMoodId,after:renamed.suggestedMoodId,status:renamed.status,expected:'Mapping stays attached to stable mood ID or explicit invalid mapping status'});
fs.writeFileSync('docs/AI_AUDIT_REPRO_RESULTS.json',JSON.stringify({date:'2026-10-07',isolatedDatabase:true,findings},null,2));console.log(JSON.stringify(findings,null,2));
}finally{if(child&&child.exitCode===null){child.kill();await new Promise(r=>child.once('exit',r));}if(dbName.startsWith('moodify_audit_'))await c.db(dbName).dropDatabase();await c.close();}})().catch(e=>{console.error(e.message);process.exitCode=1});

