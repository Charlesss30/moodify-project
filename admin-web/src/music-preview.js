import { fillMissingImages } from './media.js'
import { request } from './api.js'
const esc = v => String(v ?? '').replace(/[&<>'"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]))
let activePlayer, activeDialog, generation = 0
const validId = id => /^[a-zA-Z0-9]{22}$/.test(id || '')
export function stopMusicPreview() {
  generation++
  activePlayer?.remove(); activePlayer = null
  if(activeDialog){activeDialog.close();activeDialog.remove();activeDialog=null}
}
export async function playMusicPreview(endpoint, host) {
  activePlayer?.remove(); activePlayer=null
  const current=++generation
  host.textContent='Opening Spotify player…'
  try {
    const d=await request(endpoint)
    if(current!==generation || !host.isConnected)return
    const url=new URL(d.embedUrl)
    if(d.mode!=='embed'||url.origin!=='https://open.spotify.com'||!/^\/embed\/track\/[a-zA-Z0-9]{22}$/.test(url.pathname))throw new Error('A valid Spotify player is not available for this song.')
    const frame=document.createElement('iframe')
    frame.src=url.href;frame.title='Spotify player';frame.width='100%';frame.height='152'
    frame.allow='autoplay; clipboard-write; encrypted-media; fullscreen; picture-in-picture'
    frame.style.border='0';frame.style.borderRadius='12px';frame.allowFullscreen=true
    const note=document.createElement('p');note.textContent='Press play to preview. Playback depends on your Spotify session and region.'
    const link=document.createElement('a');link.href='https://open.spotify.com/track/'+url.pathname.split('/').pop();link.target='_blank';link.rel='noopener noreferrer';link.textContent='Open on Spotify'
    activePlayer=frame;host.replaceChildren(frame,note,link)
  }catch(e){if(current===generation&&host.isConnected)host.textContent=e.message}
}
export async function openMusicDetails(id) {
  stopMusicPreview();const current=generation
  const data=await request('/Music/'+encodeURIComponent(id))
  if(current!==generation)return
  const track=data.nhac||{}, connected=track.source==='Spotify'&&validId(track.externalId)
  const dialog=document.createElement('dialog');activeDialog=dialog;dialog.className='music-dialog'
  dialog.setAttribute('aria-labelledby','music-detail-title')
  const seconds=Number(track.duration)||0, duration=Math.floor(seconds/60)+':'+String(seconds%60).padStart(2,'0')
  dialog.innerHTML=`<button class="close-button" data-close aria-label="Close">&times;</button><div class="music-detail-grid"><section class="music-detail-summary"><img class="music-detail-cover" src="${esc(data.hinhAnh)}" alt="Cover art"><h2 id="music-detail-title">${esc(data.tieuDe)}</h2></section><dl><dt>Artist</dt><dd>${esc(track.tenNgheSi||'Not available')}</dd><dt>Album</dt><dd>${esc(track.album||'Not available')}</dd><dt>Genres</dt><dd>${esc(track.genre||'Unassigned')}</dd><dt>Duration</dt><dd>${duration}</dd><dt>Released</dt><dd>${esc(track.releaseDate||'Not available')}</dd><dt>Source</dt><dd>${esc(track.source||'Manual entry')}</dd><dt>Moods</dt><dd>${data.moodAvailable === false ? 'Not assigned' : 'Valence '+esc(data.valence)+' &middot; Arousal '+esc(data.arousal)}</dd></dl></div><div data-player></div>`
  dialog.querySelector('[data-close]').onclick=stopMusicPreview
  dialog.addEventListener('cancel',e=>{e.preventDefault();stopMusicPreview()})
  fillMissingImages(dialog)
  document.body.appendChild(dialog);dialog.showModal()
  if(connected) await playMusicPreview('/Music/'+encodeURIComponent(id)+'/preview',dialog.querySelector('[data-player]'))
  else dialog.querySelector('[data-player]').textContent='Spotify playback has not been linked to this song.'
}
