import { request } from './api.js'
export function bindMovieMood(form) {
  if (!form || !['movies','music'].includes(form.dataset.type)) return
  const v=form.elements.valence, a=form.elements.arousal, source=form.elements.moodSource
  const note=form.querySelector('[data-mood-status]')
  let revision=0, locked=v.value!=='' || a.value!==''
  const describe=()=>{note.textContent=source.value==='genre-default'
    ? ''
    : locked ? 'Current values are preserved. Edit them manually or select Suggest from genre.' : 'Select a genre or enter both values from -1 to 1.'}
  for(const input of [v,a])input.addEventListener('input',()=>{revision++;locked=true;source.value='manual';describe()})
  const suggest=async(force=false)=>{
    const current=++revision
    if(locked&&!force){describe();return}
    const genre=form.elements.genre.value
    if(!locked){v.value='';a.value='';source.value=''}
    note.textContent='Suggesting mood coordinates…'
    try{
      const d=await request((form.dataset.type === 'movies' ? '/Movies' : '/Music')+'/mood-suggestion?'+new URLSearchParams({genres:genre}))
      if(!form.isConnected||current!==revision)return
      if(d.valence===null){
        if(!locked){v.value='';a.value='';source.value=''}
        note.textContent='Unknown genre. Enter Valence and Arousal manually.'
        return
      }
      v.value=d.valence;a.value=d.arousal;source.value=d.source;locked=false
      note.textContent=d.unknownGenres.length?'Unmapped genres: '+d.unknownGenres.join(', '):''
    }catch(e){if(form.isConnected&&current===revision)note.textContent=e.message}
  }
  form.elements.genre.addEventListener('change',()=>{suggest()})
  form.querySelector('[data-mood-suggest]').onclick=()=>suggest(true)
  describe()
  if(!locked && form.elements.genre.value) suggest()
}
