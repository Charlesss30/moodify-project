import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {MongoClient, BSON, Long} from 'mongodb';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const collections=['moodHistory','interactions','ratings'];
const origin='synthetic_demo';
const dayMs=86400000, offsetMs=420*60000;
const digest=value=>crypto.createHash('sha256').update(typeof value==='string'?value:BSON.EJSON.stringify(value,{relaxed:false})).digest('hex');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const asNumber=value=>Long.isLong(value)?value.toNumber():Number(value);
const iso=value=>new Date(value).toISOString();
function random(seed){let n=parseInt(digest(seed).slice(0,8),16)>>>0;return()=>{n=(Math.imul(n,1664525)+1013904223)>>>0;return n/4294967296;};}
function argsOf(args){
 const result={};
 for(let i=0;i<args.length;i++){
  const key=args[i];if(!key.startsWith('--'))throw Error('Unexpected argument: '+key);
  result[key.slice(2)]=args[i+1]&&!args[i+1].startsWith('--')?args[++i]:true;
 }
 return result;
}
function connection(options){
 const base=read(path.join(root,'backend/appsettings.json'));
 const devPath=path.join(root,'backend/appsettings.Development.json');
 const dev=fs.existsSync(devPath)?read(devPath):{};
 const config={...base.Mongo,...dev.Mongo};
 return {uri:process.env.MONGO_URI||config.ConnectionString||'mongodb://127.0.0.1:27017',
  database:options.database||process.env.MONGO_DATABASE||config.Database||'mood_recommendation'};
}
function nativeDump(uri,database,out){
 const binary=process.env.MONGODUMP_BIN||(process.platform==='win32'?path.join(process.env.ProgramFiles||'C:/Program Files','MongoDB/Tools/100/bin/mongodump.exe'):'mongodump');
 fs.mkdirSync(out,{recursive:true});
 const run=spawnSync(binary,['--uri',uri,'--db',database,'--out',out],{encoding:'utf8',windowsHide:true,timeout:120000,maxBuffer:8*1024*1024});
 fs.writeFileSync(path.join(out,'mongodump.log'),(run.stdout||'')+(run.stderr||''));
 if(run.error||run.status!==0)throw Error('mongodump failed; no seed data inserted. Inspect private backup log.');
 return path.join(out,database);
}
function bigId(value){return BigInt(value?.toString()||'0');}
async function reserveIds(db,name,count){
 if(!count)return [];
 const last=await db.collection(name).find().sort({_id:-1}).limit(1).next();
 await db.collection('counters').updateOne({_id:name},{$max:{value:Long.fromBigInt(bigId(last?._id))}},{upsert:true});
 const row=await db.collection('counters').findOneAndUpdate({_id:name},{$inc:{value:Long.fromNumber(count)}},{returnDocument:'after'});
 const finish=bigId(row.value);
 return Array.from({length:count},(_,i)=>Long.fromBigInt(finish-BigInt(count)+1n+BigInt(i)));
}
export async function buildPlan(db,options){
 const email=String(options.email||'').trim().toLowerCase();
 if(!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email))throw Error('Provide --email for an existing account.');
 const users=await db.collection('accounts').find({emailNormalized:email.toUpperCase()},{projection:{_id:1,email:1,username:1,role:1,isActive:1}}).toArray();
 if(users.length!==1||users[0].role!=='User'||users[0].isActive!==true)throw Error('Exactly one active User account must match the email.');
 const user=users[0], accountId=asNumber(user._id), days=Number(options.days||35);
 if(!Number.isInteger(days)||days<7||days>60)throw Error('days must be 7..60.');
 const date=options['through-date']||new Date(Date.now()+offsetMs).toISOString().slice(0,10);
 if(!/^\d{4}-\d{2}-\d{2}$/.test(date)||new Date(date+'T00:00:00Z').toISOString().slice(0,10)!==date)throw Error('Invalid through-date.');
 const today=Date.parse(date+'T00:00:00+07:00'), from=today-(days-1)*dayMs;
 const until=Math.min(Date.now(),today+dayMs-1);
 if(from>=until)throw Error('Date range is in the future.');
 const batch=String(options['batch-id']||`user-demo-${accountId}-${date.replaceAll('-','')}-v1`);
 if(!/^[a-zA-Z0-9-]{8,80}$/.test(batch))throw Error('Invalid batch ID.');
 const marker={accountId,seedBatchId:batch,dataOrigin:origin,isSynthetic:true,trainingEligible:false};
 const match={accountId,seedBatchId:batch,dataOrigin:origin};
 const existingCounts={};
 for(const name of collections)existingCounts[name]=await db.collection(name).countDocuments(match);
 if(Object.values(existingCounts).some(Boolean))return {user,batch,match,existing:true,existingCounts,from,until,date,days};
 const moods=await db.collection('moods').find().sort({_id:1}).toArray();
 const names=['Focused','Happy','Sad','Relaxed','Excited','Romantic','Nostalgic'];
 const byName=new Map(moods.map(m=>[m.name,m]));
 if(names.some(name=>!byName.has(name)))throw Error('Expected seven app moods must exist.');
 const catalog=await db.collection('contents').find({deleted:{$ne:true},contentType:{$in:['Movie','Song']}}).toArray();
 const movies=new Map((await db.collection('movies').find().toArray()).map(x=>[x._id,x]));
 const songs=new Map((await db.collection('music').find().toArray()).map(x=>[x._id,x]));
 const usable=catalog.filter(c=>typeof c.title==='string'&&c.title&&typeof c.imageUrl==='string'&&c.imageUrl.startsWith('https://')&&
  (c.contentType==='Movie'?movies.has(c._id):songs.has(c._id)));
 if(usable.filter(x=>x.contentType==='Movie').length<8||usable.filter(x=>x.contentType==='Song').length<8)throw Error('Need sufficient linked movie/music catalog.');
 const rand=random(batch), used=new Map(), rated=new Set((await db.collection('ratings').find({accountId},{projection:{contentId:1}}).toArray()).map(x=>x.contentId));
 const rows={moodHistory:[],interactions:[],ratings:[]};
 const text={
  Focused:['Mình đang tập trung giải quyết công việc.','Hôm nay cần một khoảng yên tĩnh để học tập.'],
  Happy:['Hôm nay mình cảm thấy vui và hài lòng.','Một ngày nhẹ nhàng với nhiều chuyện vui.'],
  Sad:['Hôm nay mình hơi buồn và muốn được lắng nghe.','Mình thấy chùng xuống sau một ngày dài.'],
  Relaxed:['Buổi tối mình thấy bình yên và thư giãn.','Mình muốn tận hưởng một khoảng nghỉ nhẹ nhàng.'],
  Excited:['Hôm nay mình hào hứng và có nhiều năng lượng.','Mình đang háo hức với những kế hoạch sắp tới.'],
  Romantic:['Mình muốn một chút lãng mạn cho buổi tối.','Hôm nay mình thấy ấm áp khi nghĩ về người thương.'],
  Nostalgic:['Mình đang hoài niệm những ngày xưa.','Một vài ký ức cũ làm mình bồi hồi.']
 };
 const starPattern=[5,4,4,3,5,4,2,5,3,4,5,1];
 const comments={
  Movie:{1:'Bộ phim chưa hợp với tâm trạng hôm nay.',2:'Câu chuyện chưa tạo nhiều hứng thú với mình.',3:'Nội dung ổn, có vài đoạn hơi chậm.',4:'Cốt truyện và không khí phim khá phù hợp.',5:'Một bộ phim để lại nhiều cảm xúc và rất đáng xem.'},
  Song:{1:'Bài hát chưa hợp gu nghe của mình.',2:'Nhịp điệu chưa phù hợp vào lúc này.',3:'Giai điệu dễ nghe, mình muốn khám phá thêm.',4:'Bài hát khá hợp tâm trạng và có giai điệu tốt.',5:'Mình rất thích bài hát này, muốn nghe lại.'}
 };
 function choose(type,v,a){
  const list=usable.filter(c=>c.contentType===type).map(c=>({c,score:Math.hypot(asNumber(c.valence)-v,asNumber(c.arousal)-a)+(used.get(c._id)||0)*.42+rand()*.18})).sort((a,b)=>a.score-b.score);
  const c=list[0].c;used.set(c._id,(used.get(c._id)||0)+1);return c;
 }
 let event=0;
 function interaction(content,action,createdAt,recommendationId){
  const id=`${accountId}:demo-${digest(batch).slice(0,12)}-${String(++event).padStart(5,'0')}`;
  rows.interactions.push({...marker,_id:id,contentId:content._id,action,createdAt:new Date(createdAt),title:content.title,imageUrl:content.imageUrl,contentType:content.contentType,recommendationId});
 }
 for(let d=0;d<days;d++){
  // Quiet days provide realistic gaps rather than a fabricated uninterrupted streak.
  if(d>0&&d%11===7)continue;
  const day=from+d*dayMs, weekday=new Date(day+offsetMs).getUTCDay(),weekend=weekday===0||weekday===6;
  const wanted=2+(d%4===0?2:d%3===0?1:0);
  let dayRatings=0;const ratingBudget=d%4===0?1:2;
  const slots=weekend?[[9,40],[14,10],[19,25],[22,5]]:[[7,40],[12,20],[18,45],[21,10]];
  for(let slot=0;slot<wanted;slot++){
   const [hour,minute]=slots[slot];
   const stamp=day+hour*3600000+(minute+Math.floor(rand()*21)-10)*60000+Math.floor(rand()*45)*1000;
   if(stamp+40*60000>until)continue;
   const moodName=names[(d*3+slot+(weekend?2:0))%names.length],mood=byName.get(moodName);
   const v=Number((asNumber(mood.minValence)+(asNumber(mood.maxValence)-asNumber(mood.minValence))*(.18+rand()*.64)).toFixed(4));
   const a=Number((asNumber(mood.minArousal)+(asNumber(mood.maxArousal)-asNumber(mood.minArousal))*(.18+rand()*.64)).toFixed(4));
   const recommendationId='rec_demo_'+digest(batch+':'+rows.moodHistory.length).slice(0,32);
   const recommended=[choose('Song',v,a),choose('Movie',v,a),choose('Song',v,a),choose('Movie',v,a)];
   rows.moodHistory.push({...marker,moodId:asNumber(mood._id),createdAt:new Date(stamp),valence:v,arousal:a,
    inputText:'[Dữ liệu mẫu] '+text[moodName][(d+slot)%2],source:'manual',decisionSource:'manual',algorithm:'demo_manual_v1',recommendationId,
    recommendedItems:recommended.map(c=>({contentId:c._id,title:c.title,contentType:c.contentType,imageUrl:c.imageUrl}))});
   for(let i=0;i<recommended.length;i++){
    if(i>1&&rand()>.32)continue;
    const c=recommended[i],openAt=stamp+(i*5+1)*60000;
    interaction(c,'detail_open',openAt,recommendationId);
    if(c.contentType==='Song'&&songs.get(c._id)?.source==='Spotify')interaction(c,'player_loaded',openAt+40000,recommendationId);
    if(c.contentType==='Movie'&&movies.get(c._id)?.trailerUrl)interaction(c,'trailer_open',openAt+60000,recommendationId);
    if(rand()<.24)interaction(c,'external_open',openAt+90000,recommendationId);
    if(!rated.has(c._id)&&rand()<.65&&dayRatings<ratingBudget&&rows.ratings.length<60){
     const stars=starPattern[rows.ratings.length%starPattern.length];rated.add(c._id);dayRatings++;
     rows.ratings.push({...marker,contentId:c._id,stars,comment:'[Dữ liệu mẫu] '+comments[c.contentType][stars],
      createdAt:new Date(openAt+(3+Math.floor(rand()*8))*60000),recommendationId,title:c.title,imageUrl:c.imageUrl,contentType:c.contentType});
    }
   }
  }
 }
 if(rows.moodHistory.length<days||rows.interactions.length<days*3||rows.ratings.length<12)throw Error('Generated data is unexpectedly sparse.');
 for(const row of [...rows.moodHistory,...rows.interactions,...rows.ratings])if(+row.createdAt<from||+row.createdAt>until)throw Error('Generated timestamp outside requested range.');
 return {user,batch,marker,match,from,until,date,days,rows};
}
export async function verifyBatch(db,plan){
 const rows={};for(const name of collections)rows[name]=await db.collection(name).find(plan.match).sort({createdAt:1}).toArray();
 if(rows.moodHistory.length<plan.days||rows.interactions.length<plan.days*3||rows.ratings.length<12)throw Error('Demo batch is incomplete; inspect its audit report or roll back this batch.');
 const moods=new Map((await db.collection('moods').find().toArray()).map(x=>[asNumber(x._id),x]));
 const contents=new Map((await db.collection('contents').find({deleted:{$ne:true}}).toArray()).map(x=>[x._id,x]));
 const sessions=new Map(rows.moodHistory.map(x=>[x.recommendationId,x]));
 const opened=new Map();
 for(const e of rows.interactions){
  if(e.action==='detail_open')opened.set(e.contentId,Math.min(opened.get(e.contentId)||Infinity,+e.createdAt));
  const c=contents.get(e.contentId),session=sessions.get(e.recommendationId);
  if(!c||!session||!session.recommendedItems.some(x=>x.contentId===e.contentId)||e.contentType!==c.contentType||+e.createdAt<+session.createdAt)throw Error('Invalid interaction reference.');
  if(e.action==='player_loaded'&&c.contentType!=='Song'||e.action==='trailer_open'&&c.contentType!=='Movie')throw Error('Wrong player/trailer type.');
 }
 for(const row of rows.moodHistory){
  const mood=moods.get(asNumber(row.moodId));
  if(!mood||row.source!=='manual'||row.analysisId||row.analyzerVersion||asNumber(row.valence)<asNumber(mood.minValence)||asNumber(row.valence)>asNumber(mood.maxValence)||asNumber(row.arousal)<asNumber(mood.minArousal)||asNumber(row.arousal)>asNumber(mood.maxArousal))throw Error('Invalid mood geometry/provenance.');
  if(row.recommendedItems.some(x=>!contents.has(x.contentId)||x.title!==contents.get(x.contentId).title||!x.imageUrl))throw Error('Invalid recommendation snapshot.');
 }
 for(const row of rows.ratings){
  const session=sessions.get(row.recommendationId);
  if(!session||!session.recommendedItems.some(x=>x.contentId===row.contentId)||!contents.has(row.contentId)||!opened.has(row.contentId)||opened.get(row.contentId)>+row.createdAt||row.stars<1||row.stars>5)throw Error('Invalid rating or experience order.');
 }
 const all=Object.values(rows).flat();
 if(all.some(x=>x.isSynthetic!==true||x.trainingEligible!==false||x.dataOrigin!==origin||+x.createdAt<plan.from||+x.createdAt>plan.until))throw Error('Missing demo provenance or invalid date.');
 const group=(values,key)=>Object.fromEntries([...new Set(values.map(key))].sort().map(k=>[k,values.filter(x=>key(x)===k).length]));
 return {accountId:asNumber(plan.user._id),email:plan.user.email,batchId:plan.batch,days:plan.days,from:iso(plan.from),until:iso(plan.until),
  counts:Object.fromEntries(collections.map(n=>[n,rows[n].length])),activeDays:new Set(rows.moodHistory.map(x=>new Date(+x.createdAt+offsetMs).toISOString().slice(0,10))).size,
  moods:group(rows.moodHistory,x=>moods.get(asNumber(x.moodId)).name),actions:group(rows.interactions,x=>x.action),
  contentTypes:group(rows.interactions,x=>x.contentType),stars:group(rows.ratings,x=>String(x.stars)),uniqueContents:new Set(rows.interactions.map(x=>x.contentId)).size,
  dataOrigin:origin,trainingEligible:false};
}
export async function execute(options){
 const config=connection(options),client=new MongoClient(config.uri);
 try{
  await client.connect();const db=client.db(config.database),plan=await buildPlan(db,options);
  if(options.rollback){
   if(!options['batch-id'])throw Error('Rollback requires an explicit --batch-id.');
   const deleted={};for(const name of ['ratings','interactions','moodHistory'])deleted[name]=(await db.collection(name).deleteMany(plan.match)).deletedCount;
   console.log(JSON.stringify({batchId:plan.batch,deleted,countersRewound:false}));return;
  }
  if(plan.existing){
   const report=await verifyBatch(db,plan);console.log(JSON.stringify({alreadyPresent:true,...report}));return report;
  }
  if(options.verify)throw Error('This batch does not exist.');
  if(!options.apply){
   const report={dryRun:true,account:plan.user,batchId:plan.batch,days:plan.days,from:iso(plan.from),until:iso(plan.until),counts:Object.fromEntries(collections.map(n=>[n,plan.rows[n].length]))};
   console.log(JSON.stringify(report));return report;
  }
  const stamp=new Date().toISOString().replace(/[-:.]/g,'');
  const auditDir=path.join(root,'.local-backups',`user-demo-${asNumber(plan.user._id)}-${stamp}`);
  fs.mkdirSync(auditDir,{recursive:true});
  const before={};
  for(const name of [...collections,'accounts','contents','movies','music','moods','savedContents','genres']){
   before[name]=await db.collection(name).find().sort({_id:1}).toArray();
  }
  fs.writeFileSync(path.join(auditDir,'before.ejson'),BSON.EJSON.stringify(before,{relaxed:false}));
  const dumpPath=nativeDump(config.uri,db.databaseName,path.join(auditDir,'dump-before'));
  for(const name of ['moodHistory','ratings']){
   const ids=await reserveIds(db,name,plan.rows[name].length);
   plan.rows[name].forEach((row,i)=>row._id=ids[i]);
  }
  fs.writeFileSync(path.join(auditDir,'generated.ejson'),BSON.EJSON.stringify(plan.rows,{relaxed:false}));
  try{
   for(const name of collections)if(plan.rows[name].length)await db.collection(name).insertMany(plan.rows[name],{ordered:true});
   const report=await verifyBatch(db,plan);
   for(const [name,original] of Object.entries(before)){
    if(!original.length)continue;
    const actual=await db.collection(name).find({_id:{$in:original.map(x=>x._id)}}).sort({_id:1}).toArray();
    if(digest(actual)!==digest(original))throw Error('Existing documents changed during seed: '+name);
   }
   report.database=db.databaseName;report.backupBefore=dumpPath;report.auditDirectory=auditDir;report.originalDocumentsUnchanged=true;
   fs.writeFileSync(path.join(auditDir,'manifest.json'),JSON.stringify(report,null,2));
   fs.mkdirSync(path.join(root,'.local-build'),{recursive:true});
   fs.writeFileSync(path.join(root,'.local-build','user-demo-last-result.json'),JSON.stringify(report,null,2));
   console.log(JSON.stringify(report));return report;
  }catch(error){
   for(const name of ['ratings','interactions','moodHistory'])await db.collection(name).deleteMany(plan.match);
   fs.writeFileSync(path.join(auditDir,'failed.json'),JSON.stringify({message:error.message,generatedRecordsRemoved:true}));
   throw error;
  }
 }finally{await client.close();}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href){
 execute(argsOf(process.argv.slice(2))).catch(error=>{console.error(error.message);process.exitCode=1;});
}