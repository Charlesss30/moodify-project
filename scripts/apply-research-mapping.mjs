// Explicit user-authorized maintenance migration, not an authenticated engineer action.
// Default: prepare a reviewable plan and backup. Apply: --apply <plan.ejson>.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {MongoClient, BSON} from 'mongodb';
const root=path.resolve(import.meta.dirname,'..');
const encode=x=>BSON.EJSON.stringify(x,{relaxed:false});
const hash=x=>crypto.createHash('sha256').update(encode(x)).digest('hex');
const additions={admiration:'Happy',gratitude:'Happy',optimism:'Happy',pride:'Happy',relief:'Relaxed',disappointment:'Sad',remorse:'Sad'};
const client=new MongoClient(process.env.MONGO_URI||'mongodb://127.0.0.1:27017');
const database=process.env.Mongo__Database||'mood_recommendation';
try {
 await client.connect(); const db=client.db(database), mappings=db.collection('aiEmotionMappings');
 const current=await mappings.findOne({_id:'active'}), moods=await db.collection('moods').find({}).sort({_id:1}).toArray();
 if(!current || current.Labels.length!==28 || new Set(current.Labels.map(x=>x.Label)).size!==28) throw Error('Expected complete existing 28-label mapping.');
 const apply=process.argv.indexOf('--apply');
 if(apply<0){
  const next=BSON.EJSON.parse(encode(current)); const changes=[];
  for(const [label,name] of Object.entries(additions)){
   const targets=moods.filter(m=>m.name===name);if(targets.length!==1)throw Error('Missing/ambiguous mood '+name);
   const row=next.Labels.find(x=>x.Label===label);if(!row)throw Error('Missing label '+label);
   if(row.MoodId===targets[0]._id)continue;
   if(row.MoodId!=null)throw Error('Refusing to replace an existing manual mapping for '+label);
   changes.push({label,from:null,to:targets[0]._id,mood:name});row.MoodId=targets[0]._id;row.SeedName=null;
  }
  if(!changes.length){console.log('Already applied; no changes.');process.exitCode=0;}
  else{
   const dir=path.join(root,'.local-backups','emotion-mapping-'+new Date().toISOString().replace(/[:.]/g,'-'));fs.mkdirSync(dir,{recursive:true});
   fs.writeFileSync(path.join(dir,'before.ejson'),encode({database,mapping:current,moods}));
   next.Revision=BSON.Long.fromNumber(Number(current.Revision)+1);next.Version='emotion-map-'+crypto.randomUUID().replaceAll('-','');next.UpdatedAt=new Date();next.UpdatedBy=null;
   const plan={database,expectedHash:hash(current),moodsHash:hash(moods),before:current,next,changes};
   const file=path.join(dir,'plan.ejson');fs.writeFileSync(file,encode(plan));
   console.log(JSON.stringify({plan:file,changes,mapped:next.Labels.filter(x=>x.MoodId!=null).length},null,2));
  }
 }else{
  const file=path.resolve(process.argv[apply+1]||'');
  const backupRoot=path.join(root,'.local-backups')+path.sep;
  if(!file.startsWith(backupRoot))throw Error('Plan must be inside project .local-backups.');
  const plan=BSON.EJSON.parse(fs.readFileSync(file,'utf8'));
  if(plan.database!==database||hash(current)!==plan.expectedHash||hash(moods)!==plan.moodsHash)throw Error('Mapping/moods changed; prepare a new plan.');
  // Revalidate the exact permitted delta, including all untouched labels.
  if(plan.next.Labels.length!==28||Number(plan.next.Revision)!==Number(current.Revision)+1)throw Error('Invalid plan revision/labels.');
  for(const old of current.Labels){
   const row=plan.next.Labels.find(x=>x.Label===old.Label);
   const target=additions[old.Label]?moods.find(m=>m.name===additions[old.Label])?._id:old.MoodId;
   if(!row||row.MoodId!==target||(old.MoodId!=null&&row.MoodId!==old.MoodId))throw Error('Unexpected mapping delta.');
  }
  const archive={...current,_id:current.Version};
  await mappings.updateOne({_id:archive._id},{$setOnInsert:archive},{upsert:true});
  const auditId=crypto.randomUUID().replaceAll('-','');
  // aiAudit is reserved for authenticated Admin/AIEngineer actions by its schema.
  const maintenanceAudit=db.collection('aiMappingMaintenanceAudit');
  await maintenanceAudit.insertOne({_id:auditId,actorId:0,actorRole:'Maintenance',action:'mapping_research_apply',modelVersion:null,outcome:'pending',createdAt:new Date(),detail:`User-authorized research mapping: ${current.Version} -> ${plan.next.Version}. Sources and rationale: docs/EMOTION_MAPPING_RESEARCH.md. No account impersonated.`});
  const write=await mappings.replaceOne(current,plan.next);
  if(write.modifiedCount!==1){await maintenanceAudit.updateOne({_id:auditId},{$set:{outcome:'conflict'}});throw Error('Concurrent mapping edit; no active mapping replaced.');}
  await maintenanceAudit.updateOne({_id:auditId},{$set:{outcome:'succeeded'}});
  const actual=await mappings.findOne({_id:'active'});
  if(hash(actual)!==hash(plan.next))throw Error('Post-write verification failed; inspect backup before further edits.');
  if(hash(await db.collection('moods').find({}).sort({_id:1}).toArray())!==plan.moodsHash)throw Error('Mood snapshot changed during migration.');
  const result={version:actual.Version,revision:Number(actual.Revision),mapped:actual.Labels.filter(x=>x.MoodId!=null).length,unmapped:actual.Labels.filter(x=>x.MoodId==null).map(x=>x.Label),changes:plan.changes,auditId,backup:path.dirname(file)};
  fs.writeFileSync(path.join(path.dirname(file),'result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }
}finally{await client.close();}
