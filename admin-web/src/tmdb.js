import { request } from './api.js'
export function bindTmdb(form) {
  const button=form?.querySelector('[data-tmdb]')
  if(!button)return
  const status=form.querySelector('[data-tmdb-status]'), results=form.querySelector('[data-tmdb-results]')
  let generation=0
  const valid=g=>form.isConnected && g===generation
  form.elements.ratingSource.readOnly=true
  form.elements.metadataSource.readOnly=true
  form.elements.rating.addEventListener('input',()=>{form.elements.ratingSource.value='Manual'})
  async function fill(id,g) {
    const data=await request('/Tmdb/lookup?'+new URLSearchParams({query:id}))
    if(!valid(g))return
    for(const name of ['title','poster','description','rating','imdbId','tmdbId','director','trailerUrl','ratingSource'])form.elements[name].value=data[name]??''
    form.elements.metadataSource.value='TMDB'
    const genre=form.elements.genre
    if(data.genre && ![...genre.options].some(o=>o.value===data.genre))genre.add(new Option(data.genre,data.genre))
    genre.value=data.genre||'';genre.dispatchEvent(new Event('change'))
    form.querySelector('[data-trailer-status]').textContent=data.trailerUrl?'Official trailer found via TMDB. Preview before saving.':'No official trailer returned. You can enter a URL manually.'
    status.textContent='Autofilled: '+data.title+(data.year?' ('+data.year+')':'')+' · Source and rating: TMDB. Review and save to add to the library.'
  }
  results.onchange=async()=>{
    if(!results.value)return
    const g=++generation;button.disabled=true;results.disabled=true
    try{await fill(results.value,g)}catch(e){if(valid(g))status.textContent=e.message}
    finally{if(valid(g)){button.disabled=false;results.disabled=false}}
  }
  button.onclick=async()=>{
    const query=form.querySelector('[data-tmdb-query]').value.trim(), year=form.querySelector('[data-tmdb-year]').value
    if(!query){status.textContent='Enter a movie title, IMDb ID or TMDB ID.';return}
    if(year&&(!/^\d{4}$/.test(year)||+year<1800||+year>2100)){status.textContent='Enter a valid release year.';return}
    const g=++generation;button.disabled=true;results.hidden=true;results.replaceChildren();status.textContent='Searching TMDB…'
    try{
      if(/^(tt\d{7,10}|[1-9]\d{0,8})$/.test(query)){await fill(query,g);return}
      const params=new URLSearchParams({query});if(year)params.set('year',year)
      const data=await request('/Tmdb/search?'+params)
      if(!valid(g))return
      if(!data.items.length){status.textContent='No movie found. Try another title or IMDb ID.';return}
      results.add(new Option('Choose the correct movie…',''))
      for(const item of data.items)results.add(new Option(item.title+' ('+(item.year||'year unknown')+') · TMDB '+item.id,String(item.id)))
      results.hidden=false;status.textContent='Select a movie to fill its details.'
    }catch(e){if(valid(g))status.textContent=e.message}
    finally{if(valid(g))button.disabled=false}
  }
}
