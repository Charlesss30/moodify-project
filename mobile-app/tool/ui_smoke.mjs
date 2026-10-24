import {createServer} from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';
import {deflateSync} from 'node:zlib';
const root=process.cwd();
const {chromium}=await import(pathToFileURL(path.join(root,'admin-web/node_modules/playwright-core/index.mjs')));
const web=path.join(root,'mobile-app/build/web');
const out=path.join(root,'.local-build/mobile-ui-review');
fs.mkdirSync(out,{recursive:true});
const moods=['Happy','Sad','Relaxed','Excited','Focused','Romantic','Nostalgic'].map((tenTamTrang,i)=>({
  tamTrangID:i+1,tenTamTrang,minValence:-.2,maxValence:.8,minArousal:-.4,maxArousal:.6,
}));
const movies=Array.from({length:28},(_,i)=>({
  noiDungID:`movie-${i}`,tieuDe:['Một ngày đầy nắng','Những giấc mơ thành phố','Hành trình trở về'][i%3]+(i>2?' '+i:''),
  loaiNoiDung:'Movie',hinhAnh:`http://127.0.0.1:5377/art/${i}.png`,
  moTa:'Một câu chuyện về những cuộc gặp gỡ, những lựa chọn và cách chúng ta tìm lại niềm vui trong cuộc sống.',
  valence:.5,arousal:.2,
  phim:{director:'Đạo diễn thử nghiệm',theLoai:'Drama, Romance',diemDanhGiaTB:8.1,ratingSource:'TMDB',
    trailerUrl:'https://www.youtube.com/watch?v=12345678901'},
}));
const songs=Array.from({length:25},(_,i)=>({
  noiDungID:`song-${i}`,tieuDe:['Một chút bình yên','Giai điệu hôm nay','Đi qua mùa hạ'][i%3]+(i>2?' '+i:''),
  loaiNoiDung:'Song',hinhAnh:`http://127.0.0.1:5377/art/${i+30}.png`,
  nhac:{tenNgheSi:'Nghệ sĩ thử nghiệm',genre:'Pop',album:'Những ngày nhẹ nhàng',duration:210},
  valence:.4,arousal:-.2,
}));
const trend=Array.from({length:30},(_,i)=>({
  date:new Date(Date.UTC(2026,9,i+1)).toISOString().slice(0,10),
  valence:i%5===0?null:Math.sin(i*.6)*.55,arousal:i%5===0?null:Math.cos(i*.4)*.45,
  moodCount:i%5===0?0:2,interactions:i%4===0?0:(i%7)+1,
}));
const savedIds=new Set(['movie-1','song-1']);
let requests=[];
function api(url,method){
  const route=url.pathname.slice(4);
  requests.push({route,method});
  if(route==='/Auth/login')return {token:'local-ui-fixture',user:{taiKhoanID:1,tenDangNhap:'Minh An',email:'an@example.invalid',vaiTro:'User'}};

  if(route.startsWith('/SavedContent/')){
    const id=decodeURIComponent(route.split('/').at(-1));
    if(method==='PUT')savedIds.add(id);
    if(method==='DELETE')savedIds.delete(id);
    return {contentId:id,saved:savedIds.has(id)};
  }
  if(route==='/SavedContent'){
    const type=url.searchParams.get('type');
    const items=[...movies,...songs].filter(x=>savedIds.has(x.noiDungID) &&
      (!type || x.loaiNoiDung===(type==='Music'?'Song':'Movie')))
      .map(content=>({contentId:content.noiDungID,content,available:true,createdAt:new Date().toISOString()}));
    return {page:1,pageSize:12,total:items.length,items};
  }
  if(route==='/History/ratings')return {page:1,pageSize:50,total:2,items:[movies[0],songs[0]].map((content,i)=>({
    danhGiaID:i+1,noiDungID:content.noiDungID,soSao:i===0?5:4,
    nhanXet:i===0?'Một bộ phim nhẹ nhàng, giúp mình có thêm năng lượng.':'Giai điệu dễ nghe và hợp tâm trạng hôm nay.',
    thoiGian:new Date().toISOString(),recommendationId:null,available:true,content,
  }))};
  if(route==='/MoodMapping')return moods;
  if(route==='/Movies')return movies;
  if(route==='/Music')return songs;
  if(route.startsWith('/Movies/'))return movies.find(x=>x.noiDungID===decodeURIComponent(route.split('/').at(-1)));
  if(route.endsWith('/preview'))return {mode:'embed',embedUrl:'https://open.spotify.com/embed/track/11dFghVXANMlKmJXsNCbNl'};
  if(route.startsWith('/Music/'))return songs.find(x=>x.noiDungID===decodeURIComponent(route.split('/').at(-1)));
  if(route==='/Recommendation')return {mood:'Happy',algorithm:'hybrid-feedback-v1',
    items:[...songs.slice(0,4),...movies.slice(0,4)].map(content=>({content,match:87}))};
  if(route==='/History')return {page:1,pageSize:50,total:24,items:Array.from({length:24},(_,i)=>({
    lichSuID:i+1,tamTrangID:i%7+1,tenTamTrang:moods[i%7].tenTamTrang,thoiGian:new Date(Date.now()-i*60000).toISOString(),
    valence:.4,arousal:-.2,recommendedItems:[songs[i%songs.length],movies[i%movies.length]],
  }))};
  if(route==='/Interactions' && method==='GET')return {page:1,total:2,items:movies.slice(0,2).map(content=>({
    contentId:content.noiDungID,title:content.tieuDe,imageUrl:content.hinhAnh,contentType:'Movie',
    action:'detail_open',createdAt:'2026-10-24T05:00:00Z',
  }))};
  if(route==='/Statistics')return {
    summary:{moodEntries:48,interactions:92,activeDays:23,moodDays:5,openedContents:12,ratings:8,averageStars:4.6},
    previous:{moodEntries:26,interactions:64},trend,
    moods:[{name:'Happy',count:20},{name:'Relaxed',count:18},{name:'Sad',count:10}],
    contentTypes:[{name:'Movie',count:38},{name:'Song',count:54}],
    genres:[{name:'Pop',count:30},{name:'Drama',count:20},{name:'Romance',count:14}],
    hours:[{name:'18:00',count:26},{name:'20:00',count:20},{name:'22:00',count:10}],
  };
  return {};
}
const mime={'.html':'text/html','.js':'text/javascript','.json':'application/json','.wasm':'application/wasm','.ttf':'font/ttf','.woff2':'font/woff2','.png':'image/png','.svg':'image/svg+xml','.ico':'image/x-icon'};

function crc32(buf){let crc=0xffffffff;for(const b of buf){crc^=b;for(let i=0;i<8;i++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}return (crc^0xffffffff)>>>0;}
function pngChunk(name,buf){
  const type=Buffer.from(name),size=Buffer.alloc(4),crc=Buffer.alloc(4);
  size.writeUInt32BE(buf.length);crc.writeUInt32BE(crc32(Buffer.concat([type,buf])));
  return Buffer.concat([size,type,buf,crc]);
}
function artPng(i){
  const width=180,height=270,palette=[[106,119,171],[155,99,126],[87,147,136],[184,147,98]][i%4];
  const raw=Buffer.alloc(height*(width*3+1));
  for(let y=0;y<height;y++)for(let x=0;x<width;x++){
    const circle=Math.hypot(x-90,y-95)<55;
    const light=(1-y/height*.6)+(circle?.18:0);
    for(let c=0;c<3;c++)raw[y*(width*3+1)+1+x*3+c]=Math.min(255,Math.round(palette[c]*light));
  }
  const header=Buffer.alloc(13);header.writeUInt32BE(width);header.writeUInt32BE(height,4);header[8]=8;header[9]=2;
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),pngChunk('IHDR',header),pngChunk('IDAT',deflateSync(raw)),pngChunk('IEND',Buffer.alloc(0))]);
}
const server=createServer((req,res)=>{
  const url=new URL(req.url,'http://127.0.0.1:5377');
  if(url.pathname.startsWith('/api/')){
    res.writeHead(200,{'content-type':'application/json; charset=utf-8','access-control-allow-origin':'*'});
    res.end(JSON.stringify(api(url,req.method)));return;
  }
  if(url.pathname.startsWith('/art/')){
    const i=Number(url.pathname.match(/\d+/)?.[0]||0);
    res.writeHead(200,{'content-type':'image/png'});res.end(artPng(i));return;
  }
  const file=path.join(web,url.pathname==='/'?'index.html':decodeURIComponent(url.pathname));
  if(!file.startsWith(web)||!fs.existsSync(file)||!fs.statSync(file).isFile()){res.writeHead(404);res.end();return;}
  res.writeHead(200,{'content-type':mime[path.extname(file)]||'application/octet-stream'});fs.createReadStream(file).pipe(res);
});
await new Promise(resolve=>server.listen(5377,'127.0.0.1',resolve));
let browser,activePage;
try{
  browser=await chromium.launch({channel:'msedge',headless:true});
  const context=await browser.newContext({viewport:{width:390,height:844},deviceScaleFactor:1});
  await context.route('https://www.youtube.com/embed/**',r=>r.fulfill({contentType:'text/html; charset=utf-8',body:'<div style="background:#151820;color:#eee;height:100%;display:grid;place-items:center;font:16px Arial">YouTube player · UI fixture</div>'}));
  await context.route('https://open.spotify.com/embed/**',r=>r.fulfill({contentType:'text/html; charset=utf-8',body:'<div style="border-radius:12px;background:#234236;color:#eee;height:100%;display:grid;place-items:center;font:16px Arial">Spotify player · UI fixture</div>'}));
  // Intercept every API request, including builds with the default localhost:5170.
  // This runner must never authenticate against or mutate a real backend.
  await context.route('**/api/**',route=>{
    const request=route.request(),url=new URL(request.url());
    route.fulfill({
      status:request.method()==='OPTIONS'?204:200,
      headers:{
        'content-type':'application/json; charset=utf-8',
        'access-control-allow-origin':'*',
        'access-control-allow-headers':'authorization,content-type',
        'access-control-allow-methods':'GET,POST,PUT,DELETE,OPTIONS',
      },
      body:request.method()==='OPTIONS'?'':JSON.stringify(api(url,request.method())),
    });
  });
  const page=await context.newPage();activePage=page;
  const click=async(name)=>{
    const locator=page.getByRole('button',{name}).or(page.getByRole('tab',{name}))
      .or(page.getByRole('radio',{name})).or(page.getByRole('checkbox',{name}));
    await locator.last().click();
    await page.mouse.move(1,400);
    await page.waitForTimeout(350);
  };
  page.setDefaultTimeout(20000);
  const errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  page.on('console',m=>{if(m.type()==='error')fs.appendFileSync(path.join(out,'console.log'),m.text()+'\n');});
  await page.goto('http://127.0.0.1:5377');
  await page.waitForTimeout(5000);
  await page.locator('flt-semantics-placeholder').evaluateAll(els=>els.forEach(el=>el.click()));
  await page.waitForTimeout(1500);
  await page.screenshot({path:path.join(out,'01-login.png')});
  fs.writeFileSync(path.join(out,'semantics.txt'),await page.locator('body').innerHTML());
  console.log('Login screenshot saved; textboxes:',await page.getByRole('textbox').count());
  const fields=page.getByRole('textbox');
  await fields.nth(0).click();await page.keyboard.type('preview');
  await fields.nth(1).click();await page.keyboard.type('preview123');
  await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();
  await page.waitForTimeout(1500);
  await page.screenshot({path:path.join(out,'02-quiz.png')});
  fs.writeFileSync(path.join(out,'semantics-quiz.txt'),await page.locator('body').innerHTML());
  await click(/Vui vẻ/);await page.waitForTimeout(1200);
  await page.screenshot({path:path.join(out,'03-home.png')});
  await click(/Thư viện/);
  await page.waitForTimeout(800);
  await page.screenshot({path:path.join(out,'04-library.png')});
  await click(/^Nhạc$/);await page.waitForTimeout(300);
  await page.screenshot({path:path.join(out,'04b-music-library.png')});
  await click(/Một chút bình yên/);await page.waitForTimeout(500);
  await page.screenshot({path:path.join(out,'05b-spotify.png')});
  await click(/Back|Quay lại/);
  await click(/^Phim$/);await page.waitForTimeout(300);
  await click(/Một ngày đầy nắng/);
  await page.waitForTimeout(800);
  await page.screenshot({path:path.join(out,'05-detail.png')});
  await click(/^Lưu để xem sau$/);await page.waitForTimeout(400);
  const savedMovieId=requests.filter(x=>x.method==='GET'&&x.route.startsWith('/Movies/')).at(-1).route.split('/').at(-1);
  const savedMovieTitle=movies.find(x=>x.noiDungID===savedMovieId).tieuDe;
  assert.ok(savedIds.has(savedMovieId),'Movie save must reach the API');
  await click(/Back|Quay lại/);
  await click(/Khám phá/);
  await page.waitForTimeout(500);
  await page.screenshot({path:path.join(out,'06-discovery.png')});
  await click(/Lịch sử/);
  await page.waitForTimeout(500);
  await page.screenshot({path:path.join(out,'07-history.png')});
  await page.mouse.move(195,600);
  await page.mouse.wheel(0,700);await page.waitForTimeout(400);
  await page.mouse.wheel(0,700);await page.waitForTimeout(400);
  const historyToggle=page.getByRole('button',{name:/2 nội dung được gợi ý/})
    .or(page.getByRole('checkbox',{name:/2 nội dung được gợi ý/}));
  let expandedHistory=false;
  for(const toggle of await historyToggle.all()){
    const box=await toggle.boundingBox();
    if(!box || box.height<=0 || box.y<110 || box.y+box.height>760)continue;
    await toggle.click();
    await page.mouse.move(1,400);await page.waitForTimeout(350);
    // Flutter exposes this state as aria-description and splits the expanded
    // card into a group containing its header and two fixture recommendations.
    const expandedHeader=page.getByRole('button',{name:'2 nội dung được gợi ý',exact:true});
    await expandedHeader.waitFor({state:'visible'});
    assert.equal(await expandedHeader.getAttribute('aria-description'),'Đã mở rộng');
    assert.equal(await expandedHeader.locator('..').getByRole('button').count(),3);
    expandedHistory=true;
    break;
  }
  assert.ok(expandedHistory,'A visible history expansion header must be found');
  await page.screenshot({path:path.join(out,'07b-history-scrolled-expanded.png')});
  await click(/Hoạt động/);await page.waitForTimeout(400);
  await page.screenshot({path:path.join(out,'08-activity.png')});
  await click(/Cá nhân/);await page.waitForTimeout(400);
  await page.screenshot({path:path.join(out,'09-profile.png')});
  await click(/Thông tin tài khoản/);await page.waitForTimeout(300);
  await page.screenshot({path:path.join(out,'09b-account-dialog.png')});
  await click(/Huỷ|Hủy/);
  await click(/^Đổi mật khẩu/);await page.waitForTimeout(300);
  await page.screenshot({path:path.join(out,'09c-password-dialog.png')});
  await click(/Huỷ|Hủy/);
  await page.mouse.move(195,600);await page.mouse.wheel(0,500);await page.waitForTimeout(400);
  await click(/^Đánh giá của tôi/);await page.waitForTimeout(400);
  await page.screenshot({path:path.join(out,'09d-my-ratings.png')});
  await click(/Back|Quay lại/);
  await click(/^Nội dung đã lưu/);await page.waitForTimeout(500);
  assert.ok(await page.getByRole('button',{name:'Bỏ lưu '+savedMovieTitle,exact:true}).count()>0);
  await page.screenshot({path:path.join(out,'09e-saved-library.png')});
  await page.getByRole('button',{name:'Bỏ lưu '+savedMovieTitle,exact:true}).click();await page.waitForTimeout(400);
  assert.ok(!savedIds.has(savedMovieId),'Unsave must reach the API');
  await click(/Cá nhân/);await page.waitForTimeout(300);
  await page.mouse.move(195,400);await page.mouse.wheel(0,-800);await page.waitForTimeout(400);
  await click(/Thống kê tâm trạng/);await page.waitForTimeout(500);
  await page.screenshot({path:path.join(out,'10-statistics.png')});
  await page.mouse.wheel(0,450);await page.waitForTimeout(400);
  await page.screenshot({path:path.join(out,'11-chart.png')});
  await click(/^Giải trí$/);await page.waitForTimeout(300);
  await page.screenshot({path:path.join(out,'12-entertainment.png')});
  fs.writeFileSync(path.join(out,'result.json'),JSON.stringify({errors,requests},null,2));
  assert.equal(errors.length,0);
  console.log('Mobile browser smoke passed; screenshots:',out);
}catch(error){
  if(activePage){
    await activePage.screenshot({path:path.join(out,'failure.png')});
    fs.writeFileSync(path.join(out,'failure-semantics.txt'),await activePage.locator('body').innerHTML());
  }
  throw error;
}finally{
  if(browser)await browser.close();
  server.closeAllConnections();
  await new Promise(resolve=>server.close(resolve));
}
