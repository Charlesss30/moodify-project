import fs from 'node:fs';
import path from 'node:path';
import {MongoClient, BSON} from 'mongodb';
if (!process.argv.includes('--apply')) throw new Error('Use --apply to back up and migrate the configured MongoDB database.');
const root=path.resolve(import.meta.dirname,'..');
const client=new MongoClient(process.env.MONGO_URI||'mongodb://127.0.0.1:27017',{serverSelectionTimeoutMS:5000});
const database=process.env.MONGO_DATABASE||'mood_recommendation';
const normalize=x=>String(x??'').trim().toUpperCase();
try {
 await client.connect();const db=client.db(database);
 if(await db.listCollections({name:"accounts"}).hasNext())throw new Error("English schema is active. This legacy migration must not be run again.");
 const infos=await db.listCollections().toArray(), snapshot=[];
 for(const info of infos)snapshot.push({name:info.name,options:info.options,indexes:await db.collection(info.name).listIndexes().toArray(),documents:await db.collection(info.name).find().toArray()});
 const dir=path.join(root,'.local-backups');fs.mkdirSync(dir,{recursive:true});
 const backup=path.join(dir,'mongo-'+database+'-'+new Date().toISOString().replaceAll(':','-')+'.json');
 fs.writeFileSync(backup,BSON.EJSON.stringify({database,collections:snapshot},{relaxed:false}));
 console.log('Backup saved:',path.relative(root,backup));
 for(const [collection,field] of [['TaiKhoan','Email'],['TaiKhoan','TenDangNhap'],['TheLoai','TenTheLoai'],['TamTrang','TenTamTrang']]){
  const docs=await db.collection(collection).find().toArray(), values=docs.map(x=>normalize(x[field]));
  if(new Set(values).size!==values.length || values.some(v=>!v))throw new Error('Resolve duplicate/empty '+collection+'.'+field+' before migration.');
  for(const row of docs)await db.collection(collection).updateOne({_id:row._id},{$set:{[field+'Normalized']:normalize(row[field])}});
 }
 await db.collection('NoiDungGiaiTri').updateMany({Deleted:{$exists:false}},{$set:{Deleted:false}});
 let quarantined=0;
 for(const row of await db.collection('Nhac').find().toArray()){
  const changes={};
  for(const [key,value] of Object.entries({Source:'Local',ExternalId:null,SourceUrl:null,ReleaseDate:null,LastSyncedAt:null,MoodSource:'legacy-unverified',TheLoaiID:null}))
   if(!(key in row))changes[key]=value;
  for(const field of ['Valence','Energy','Danceability','Acousticness','Instrumentalness','Speechiness']){
   const value=row[field];
   const numericValue=value?._bsontype==='Decimal128'?Number(value.toString()):value?._bsontype==='Long'?value.toNumber():value;
   if(value!=null && (typeof numericValue!=='number'||!Number.isFinite(numericValue)||numericValue<0||numericValue>1)){
    changes['LegacyAudioFeatures.'+field]=value;changes[field]=null;changes.NeedsReview=true;quarantined++;
   }
  }
  if(Object.keys(changes).length)await db.collection('Nhac').updateOne({_id:row._id},{$set:changes});
 }
 await db.collection('Phim').updateMany({MoodSource:{$exists:false}},{$set:{MoodSource:'legacy-unverified'}});
 // Only fill missing OMDb metadata when the exact normalized title matches. Keep reviewed coordinates.
 const configPath=path.join(root,'backend/appsettings.Development.json');
 const config=fs.existsSync(configPath)?JSON.parse(fs.readFileSync(configPath,'utf8').replace(/^\uFEFF/,'')):{};
 const key=config.Omdb?.ApiKey, enriched=[];
 const titleKey=s=>s.normalize('NFKD').replace(/[^\p{L}\p{N}]/gu,'').toLowerCase();
 if(key)for(const movie of await db.collection('Phim').find({IMDBID:null}).toArray()){
  const content=await db.collection('NoiDungGiaiTri').findOne({_id:movie._id});if(!content)continue;
  try{
   const response=await fetch('https://www.omdbapi.com/?'+new URLSearchParams({apikey:key,t:content.TieuDe,type:'movie',plot:'full'}),{signal:AbortSignal.timeout(15000)});
   const result=await response.json();
   if(result.Response!=='True'||result.Type!=='movie'||titleKey(result.Title)!==titleKey(content.TieuDe))continue;
   const changes={};if(!content.HinhAnh&&result.Poster?.startsWith('https://'))changes.HinhAnh=result.Poster;
   if(!content.MoTa&&result.Plot&&result.Plot!=='N/A')changes.MoTa=result.Plot;
   if(Object.keys(changes).length)await db.collection('NoiDungGiaiTri').updateOne({_id:movie._id},{$set:changes});
   await db.collection('Phim').updateOne({_id:movie._id},{$set:{IMDBID:result.imdbID,MetadataSource:'OMDb'}});
   enriched.push(movie._id);
  }catch{console.log('Metadata lookup unavailable for',movie._id);}
 }
 const numeric={bsonType:['int','long','double','decimal']};
 const range=(min,max,nullable=false)=>({...numeric,bsonType:nullable?[...numeric.bsonType,'null']:numeric.bsonType,minimum:min,maximum:max});
 const str={bsonType:'string'}, optionalString={bsonType:['string','null']}, int={bsonType:['int','long'],minimum:1}, date={bsonType:'date'};
 const coordinate={Valence:range(-1,1),Arousal:range(-1,1)};
 const definitions={
  TaiKhoan:{required:['_id','TenDangNhap','Email','MatKhau','VaiTro','TrangThai','EmailNormalized','TenDangNhapNormalized'],properties:{_id:int,TenDangNhap:{...str,minLength:1},Email:{...str,minLength:1},MatKhau:{...str,minLength:1},VaiTro:{enum:['Admin','QuanTriVien','NguoiDung','KySuAI']},TrangThai:{bsonType:'bool'},EmailNormalized:str,TenDangNhapNormalized:str}},
  TheLoai:{required:['_id','TenTheLoai','TenTheLoaiNormalized'],properties:{_id:str,TenTheLoai:{...str,minLength:1},TenTheLoaiNormalized:str}},
  NoiDungGiaiTri:{required:['_id','TieuDe','LoaiNoiDung','Valence','Arousal'],properties:{_id:str,TieuDe:{...str,minLength:1},LoaiNoiDung:{enum:['Movie','Song']},...coordinate,HinhAnh:optionalString,MoTa:optionalString,Deleted:{bsonType:'bool'}}},
  Phim:{required:['_id','TheLoaiIDs','MoodSource'],properties:{_id:str,TheLoaiIDs:{bsonType:'array',items:str,uniqueItems:true},DiemDanhGiaTB:range(0,10,true),IMDBID:optionalString,TMDBID:optionalString,MoodSource:str}},
  Nhac:{required:['_id','Source','MoodSource'],properties:{_id:str,TenNgheSi:optionalString,Album:optionalString,TheLoaiID:optionalString,Duration:{bsonType:['int','long','null'],minimum:0},Source:str,ExternalId:optionalString,SourceUrl:optionalString,ReleaseDate:optionalString,LastSyncedAt:{bsonType:['date','null']},MoodSource:str,...Object.fromEntries(['Valence','Energy','Danceability','Acousticness','Instrumentalness','Speechiness'].map(k=>[k,range(0,1,true)])),Popularity:range(0,100,true),Tempo:{...numeric,bsonType:[...numeric.bsonType,'null'],minimum:0}}},
  TamTrang:{required:['_id','TenTamTrang','TenTamTrangNormalized','MinValence','MaxValence','MinArousal','MaxArousal'],properties:{_id:int,TenTamTrang:str,TenTamTrangNormalized:str,MinValence:range(-1,1),MaxValence:range(-1,1),MinArousal:range(-1,1),MaxArousal:range(-1,1)}},
  DanhGia:{required:['_id','TaiKhoanID','NoiDungID','SoSao','ThoiGian'],properties:{_id:int,TaiKhoanID:int,NoiDungID:str,SoSao:{bsonType:['int','long'],minimum:1,maximum:5},NhanXet:optionalString,ThoiGian:date}},
  LichSuTamTrang:{required:['_id','TaiKhoanID','TamTrangID','ThoiGian','Valence','Arousal'],properties:{_id:int,TaiKhoanID:int,TamTrangID:int,ThoiGian:date,VanBanDauVao:optionalString,...coordinate}}
 };
 for(const [name,schema]of Object.entries(definitions)){
  const validator=name==='TamTrang'?{$and:[{$jsonSchema:{bsonType:'object',...schema}},{$expr:{$and:[{$lte:['$MinValence','$MaxValence']},{$lte:['$MinArousal','$MaxArousal']}]}}]}:{$jsonSchema:{bsonType:'object',...schema}};
  await db.command({collMod:name,validator,validationLevel:'strict',validationAction:'error'});
  const invalid=await db.collection(name).countDocuments({$nor:[validator]});if(invalid)throw new Error(name+' still has '+invalid+' invalid documents');
 }
 for(const [collection,field]of [['TaiKhoan','EmailNormalized'],['TaiKhoan','TenDangNhapNormalized'],['TheLoai','TenTheLoaiNormalized'],['TamTrang','TenTamTrangNormalized']])
  await db.collection(collection).createIndex({[field]:1},{unique:true});
 await db.collection('Nhac').createIndex({Source:1,ExternalId:1},{unique:true,partialFilterExpression:{ExternalId:{$type:'string'}}});
 await db.collection('Phim').createIndex({TheLoaiIDs:1});
 await db.collection('Nhac').createIndex({TheLoaiID:1});
 await db.collection('LichSuTamTrang').createIndex({TaiKhoanID:1,ThoiGian:-1});
 await db.collection('DanhGia').createIndex({TaiKhoanID:1,NoiDungID:1});
 await db.collection('DanhGia').createIndex({NoiDungID:1});
 for(const name of ['TaiKhoan','TamTrang','DanhGia','LichSuTamTrang']){
  const last=await db.collection(name).find().sort({_id:-1}).limit(1).next();
  await db.collection('_Counters').updateOne({_id:name},{$max:{Value:last?Number(last._id):0}},{upsert:true});
 }
 const report={date:new Date().toISOString(),database,backup:path.relative(root,backup),quarantinedFeatures:quarantined,enrichedMovieIds:enriched,contentCount:await db.collection('NoiDungGiaiTri').countDocuments()};
 fs.writeFileSync(path.join(root,'docs/MONGODB_MIGRATION_RESULT.json'),JSON.stringify(report,null,2));
 console.log(JSON.stringify(report));
}finally{await client.close();}
