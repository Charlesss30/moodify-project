import { request } from './api.js'
export function bindTrailer(form) {
  const input=form?.elements.trailerUrl, button=form?.querySelector('[data-trailer]'), status=form?.querySelector('[data-trailer-status]')
  if(!input || !button)return
  let serial=0
  async function lookup() {
    const imdbId=form.elements.imdbId.value.trim(), previous=input.value, current=++serial
    if(!/^tt\d{7,10}$/.test(imdbId)){status.textContent='Enter a valid IMDb ID first.';return}
    button.disabled=true;status.textContent='Finding an official trailer…'
    try {
      const result=await request('/Trailers/lookup?imdbId='+encodeURIComponent(imdbId))
      if(current!==serial || !form.isConnected || form.elements.imdbId.value.trim()!==imdbId)return
      if(result.status==='found' && input.value===previous){input.value=result.trailerUrl;status.textContent='Official trailer found via TMDB. Preview before saving.'}
      else status.textContent=result.message || (result.status==='found'?'URL was edited; kept your change.':'No verified trailer found. You can enter a YouTube URL.')
    } catch(e){if(current===serial)status.textContent=e.message}
    finally{if(current===serial)button.disabled=false}
  }
  button.onclick=lookup
  form.addEventListener('movie-metadata-loaded',()=>{input.value='';lookup()})
}
