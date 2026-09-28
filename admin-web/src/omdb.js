import { request } from './api.js'
export function bindOmdb(form) {
  const button = form?.querySelector('[data-omdb]')
  if (!button) return
  button.onclick = async () => {
    const query = form.querySelector('[data-omdb-query]').value.trim()
    const year = form.querySelector('[data-omdb-year]').value
    const status = form.querySelector('[data-omdb-status]')
    if (!query) { status.textContent = 'Enter a movie title or IMDb ID first.'; return }
    if (year && (!/^\d{4}$/.test(year) || Number(year) < 1800 || Number(year) > 2100)) {
      status.textContent = 'Release year must be between 1800 and 2100.'; return
    }
    button.disabled = true; status.textContent = 'Looking up OMDb…'
    try {
      const params = new URLSearchParams({query})
      if (year) params.set('year', year)
      const data = await request('/Omdb/lookup?' + params)
      if (!form.isConnected) return
      for (const name of ['title', 'poster', 'description', 'rating', 'imdbId']) form.elements[name].value = data[name] ?? ''
      const genre = form.elements.genre
      if (data.genre && ![...genre.options].some(o => o.value === data.genre)) genre.add(new Option(data.genre, data.genre))
      genre.value = data.genre || ''
      genre.dispatchEvent(new Event('change'))
      status.textContent = 'Autofilled: ' + data.title + (data.year ? ' (' + data.year + ')' : '') + ' · Source: OMDb. Review the details and select Save to add them to the library.'
    } catch (e) { if (form.isConnected) status.textContent = e.message }
    finally { button.disabled = false }
  }
}
