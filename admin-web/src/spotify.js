import { request } from './api.js'
import { playMusicPreview, stopMusicPreview } from './music-preview.js'
const esc = v => String(v ?? '').replace(/[&<>'"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]))
export async function mountSpotify(host, onImported) {
  host.innerHTML = '<p role="status">Loading moods…</p>'
  try {
    const moods = await request('/MoodMapping')
    if (!host.isConnected) return
    host.innerHTML = `<h1>Import Spotify music</h1>
      <p id="spotify-connection" class="spotify-connection" role="status">Checking Spotify…</p>
      <form id="spotify-search"><label class="field"><span>Song title / artist</span><input name="query" required maxlength="200" placeholder="Example: Adele"></label>
      <button class="primary-button" type="submit">Search music</button></form>
      <p role="alert"></p><div id="spotify-results"></div><div id="spotify-preview"></div>
      <div><button id="spotify-prev" class="secondary-button" disabled>Previous page</button> <span id="spotify-page"></span> <button id="spotify-next" class="secondary-button" disabled>Next page</button></div>
      <section class="panel"><label class="field"><span>Mood</span><select id="spotify-mood"><option value="">Select a mood</option>${moods.map(m=>`<option value="${m.tamTrangID}">${esc(m.tenTamTrang)}</option>`).join('')}</select></label>
      
      <button id="spotify-import" class="primary-button" ${moods.length?'':'disabled'}>Import selected songs</button><p id="spotify-import-status" role="status"></p></section>`
    let tracks = [], imported = new Set(), offset = 0, query = '', busy = false, searchVersion = 0
    const error = message => { if(host.isConnected) host.querySelector('[role="alert"]').textContent = message }
    const controls = () => {
      host.querySelector('#spotify-prev').disabled = busy || offset === 0
      host.querySelector('#spotify-next').disabled = busy || tracks.length < 10 || offset >= 1000
      host.querySelector('#spotify-import').disabled = busy || !moods.length
      host.querySelector('#spotify-search button').disabled = busy
      host.querySelector('#spotify-page').textContent = query ? 'Page ' + (offset / 10 + 1) : ''
    }
    const draw = () => {
      host.querySelector('#spotify-results').innerHTML = `<div class="table-scroll"><table><thead><tr><th>Select</th><th>Song · Spotify</th><th>Artist / Album</th><th>Duration</th><th>Listen</th></tr></thead><tbody>${tracks.map(t=>`<tr><td><input type="checkbox" data-song="${esc(t.externalId)}" aria-label="Select ${esc(t.title)}" ${imported.has(t.externalId)?'disabled':''}></td><td><div class="media-cell">${t.imageUrl?`<img src="${esc(t.imageUrl)}" alt="">`:''}<span><a href="${esc(t.sourceUrl)}" target="_blank" rel="noopener noreferrer">${esc(t.title)}</a>${imported.has(t.externalId)?' · Imported':''}</span></div></td><td>${esc(t.artists)}<br>${esc(t.album)}<br>${esc(t.releaseDate)}</td><td>${Math.floor(t.duration/60)}:${String(t.duration%60).padStart(2,'0')}</td><td><button class="secondary-button" data-preview="${esc(t.externalId)}">Open player</button></td></tr>`).join('') || '<tr><td colspan="5">No results.</td></tr>'}</tbody></table></div>`
      host.querySelectorAll('[data-preview]').forEach(b=>b.onclick=()=>playMusicPreview('/Spotify/'+encodeURIComponent(b.dataset.preview)+'/preview',host.querySelector('#spotify-preview')))
    }
    const search = async (text, pageOffset) => {
      if (busy || !text.trim()) return
      busy = true; const version = ++searchVersion; controls(); error(''); stopMusicPreview()
      tracks = []; host.querySelector('#spotify-results').textContent = 'Searching music…'
      try {
        const d = await request('/Spotify?'+new URLSearchParams({query:text.trim(),limit:'10',offset:String(pageOffset)}))
        if (!host.isConnected || version !== searchVersion) return
        query = text.trim(); offset = pageOffset; tracks = d.items; imported = new Set(d.importedIds); draw()
      } catch (e) { if (host.isConnected) { error(e.message); host.querySelector('#spotify-results').textContent = 'Unable to load Spotify data.' } }
      finally { busy = false; if (host.isConnected) controls() }
    }
    host.querySelector('#spotify-search').onsubmit = e => { e.preventDefault(); search(new FormData(e.currentTarget).get('query'),0) }
    host.querySelector('#spotify-prev').onclick = () => search(query,offset-10)
    host.querySelector('#spotify-next').onclick = () => search(query,offset+10)
    const connection = host.querySelector('#spotify-connection')
    const showConnection = connected => {
      if (!host.isConnected) return
      connection.className = 'spotify-connection ' + (connected ? 'is-connected' : 'is-disconnected')
      connection.textContent = connected ? 'Spotify connected' : 'Spotify unavailable. Please check your account.'
    }
    request('/Spotify/status').then(() => showConnection(true)).catch(() => showConnection(false))
    host.querySelector('#spotify-import').onclick = async () => {
      if (busy) return
      const songIds=[...host.querySelectorAll('[data-song]:checked')].map(x=>x.dataset.song)
      const tamTrangID=Number(host.querySelector('#spotify-mood').value)
      if(!songIds.length || !tamTrangID){error('Select songs and a mood before importing.');return}
      busy=true;controls();error('')
      const status=host.querySelector('#spotify-import-status');status.textContent='Importing metadata and mood mapping…'
      try {
        const d=await request('/Spotify/import',{method:'POST',body:JSON.stringify({songIds,tamTrangID})})
        if(!host.isConnected)return
        d.items.filter(x=>x.status==='imported'||x.status==='existing').forEach(x=>imported.add(x.externalId))
        const count=d.items.filter(x=>x.status==='imported').length
        const failures=d.items.filter(x=>x.status==='unavailable')
        status.textContent='Imported '+count+' songs. '+(failures.length?failures.length+' songs not imported: '+[...new Set(failures.map(x=>x.message))].join(' '):'Songs are available in your music library.')
        draw();await onImported()
      }catch(e){if(host.isConnected){error(e.message);status.textContent='You can retry the import; duplicate Spotify IDs are checked.'}}
      finally{busy=false;if(host.isConnected)controls()}
    }
  } catch (e) { if(host.isConnected)host.innerHTML='<h1>Import Spotify music</h1><p role="alert">'+esc(e.message)+'</p>' }
}
