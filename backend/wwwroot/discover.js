const tokenKey = 'moodify_user_token'
let audio
let session = 0
const esc = v => String(v ?? '').replace(/[&<>'"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]))
async function api(path, body) {
  const response=await fetch('/api'+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json',Authorization:'Bearer '+sessionStorage.getItem(tokenKey)},...(body?{body:JSON.stringify(body)}:{})})
  let data
  try {data=await response.json()}catch{data={}}
  if(!response.ok)throw Error(data.message || (response.status===401?'Your session has expired. Please sign in again.':'Unable to load data.'))
  return data
}
function stop(){if(audio){audio.pause();audio.removeAttribute('src');audio.load();audio=null}}
async function openDiscovery() {
  const version=++session
  stop()
  document.querySelector('#music-discovery')?.remove()
  const home=document.querySelector('#home-view main')||document.querySelector('#home-view')
  if(!home)return
  const section=document.createElement('section');section.id='music-discovery';section.className='music-discovery'
  section.innerHTML='<h2>Discover music for your mood</h2><p role="status">Loading moods…</p>'
  home.prepend(section)
  try {
    const moods=await api('/MoodMapping')
    if(version!==session)return
    section.innerHTML=`<h2>Discover music for your mood</h2><p>Select your current mood to get recommendations and preview songs.</p><form><select aria-label="Moods" required>${moods.map(m=>`<option value="${m.tamTrangID}">${esc(m.tenTamTrang)}</option>`).join('')}</select><button type="submit" ${moods.length?'':'disabled'}>Recommend music</button></form><p role="alert"></p><div class="music-discovery-grid"></div>`
    const message=section.querySelector('[role=alert]')
    section.querySelector('form').onsubmit=async event=>{
      event.preventDefault();stop();const submit=event.currentTarget.querySelector('button');submit.disabled=true;message.textContent='Searching music…'
      try {
        const result=await api('/Recommendation',{tamTrangID:Number(section.querySelector('select').value),loaiNoiDung:'Music',limit:12})
        if(version!==session)return
        message.textContent=result.items.length?'':'No songs yet. An administrator can add music using Import Spotify.'
        section.querySelector('.music-discovery-grid').innerHTML=result.items.map(({content:c,match})=>`<article><img src="${esc(c.hinhAnh)}" alt=""><h3>${esc(c.tieuDe)}</h3><p>${esc(c.artist)}</p><p>Mood match: ${esc(match)}%</p><button data-track="${esc(c.noiDungID)}" ${c.canPreview?'':'disabled'}>Details & preview</button><div data-player></div><label>Rating <select data-stars><option value="5">5 stars</option><option value="4">4 stars</option><option value="3">3 stars</option><option value="2">2 stars</option><option value="1">1 stars</option></select></label><button data-rate="${esc(c.noiDungID)}">Submit rating</button><p data-status role="status"></p></article>`).join('')
        section.querySelectorAll('[data-track]').forEach(button=>button.onclick=async()=>{
          stop();const box=button.parentElement.querySelector('[data-player]');box.textContent='Loading playback link…';button.disabled=true
          try {
            const [info,preview]=await Promise.all([api('/Music/'+encodeURIComponent(button.dataset.track)),api('/Music/'+encodeURIComponent(button.dataset.track)+'/preview')])
            if(version!==session)return
            stop();audio=new Audio(preview.url);const current=audio;current.controls=true;current.preload='none'
            const limit=Math.min(preview.previewSeconds||30,30)
            current.ontimeupdate=()=>{if(current.currentTime>=limit){current.pause();current.currentTime=limit}}
            current.onseeking=()=>{if(current.currentTime>limit)current.currentTime=limit}
            current.onplay=()=>{if(current.currentTime>=limit)current.currentTime=0}
            current.onerror=()=>{button.parentElement.querySelector('[data-status]').textContent='Playback failed. Select preview to get a new link.'}
            const metadata=document.createElement('p');metadata.textContent=[info.nhac?.album,info.nhac?.genre,info.nhac?.releaseDate,info.nhac?.duration+' seconds',info.nhac?.source || 'Manual entry'].filter(Boolean).join(' · ')
            box.replaceChildren(metadata,current)
            try{await current.play()}catch{button.parentElement.querySelector('[data-status]').textContent='Press play to start listening.'}
          }catch(e){box.textContent=e.message}finally{button.disabled=false}
        })
        section.querySelectorAll('[data-rate]').forEach(button=>button.onclick=async()=>{
          button.disabled=true
          try{await api('/History/ratings',{noiDungID:button.dataset.rate,soSao:Number(button.parentElement.querySelector('[data-stars]').value)});button.parentElement.querySelector('[data-status]').textContent='Rating saved.'}
          catch(e){button.parentElement.querySelector('[data-status]').textContent=e.message}finally{button.disabled=false}
        })
      }catch(e){if(version===session)message.textContent=e.message}finally{submit.disabled=false}
    }
  } catch(e){section.querySelector('[role=status]').textContent=e.message}
}
window.addEventListener('moodify-user-login',openDiscovery)
window.addEventListener('moodify-user-logout',()=>{session++;stop();document.querySelector('#music-discovery')?.remove()})
window.addEventListener('pagehide',stop)
