import { enhanceList } from './list-controls.js'
import { fillMissingImages } from './media.js'
import { bindMovieMood } from './movie-mood.js'
import { bindOmdb } from './omdb.js'
import { mountSpotify } from './spotify.js'
import { openMusicDetails, stopMusicPreview } from './music-preview.js'
import { mountManagement } from './management.js'
import { resumeAdminSession } from './session.js'
import { API_BASE_URL, deleteResource, getCollection, saveResource, request, tokenKey } from './api.js'


const labels = { overview: 'Overview', genres: 'Genres', movies: 'Movies', music: 'Music', users: 'Users', moods: 'Moods', feedback: 'Feedback', recommend: 'Recommendations', history: 'History', models: 'Models' }
const icons = { overview: '▦', genres: '◇', movies: '▣', music: '♫', users: '♙', moods: '♡', feedback: '☆', recommend: '✦', history: '◷', models: '⚙' }
const state = { activeTab: 'overview', genreType: 'Movie', query: '', sidebarOpen: false, modal: null, deleteTarget: null, data: { genres: [], movies: [], music: [] } }
const root = document.querySelector('#root')
let sessionUser = null
const escapeHtml = (value = '') => String(value).replace(/[&<>'"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#039;', '"': '&quot;' }[char]))
const icon = (name) => `<span class="icon-glyph" aria-hidden="true">${name}</span>`
const value = (item, ...keys) => keys.map((key) => item?.[key]).find((entry) => entry !== undefined && entry !== null) ?? ''
const normalizeGenre = (item) => ({ contentType: item.contentType || 'Both', defaultValence: item.defaultValence, defaultArousal: item.defaultArousal, id: value(item, 'id', 'theLoaiID'), name: value(item, 'name', 'tenTheLoai'), description: 'Moodify content genre', count: 0, raw: item })
const normalizeMovie = (item) => ({ valence: item.valence, arousal: item.arousal, moodSource: item.phim?.moodSource, imdbId: value(item.phim, 'imdbid', 'imdbID', 'IMDBID'), id: value(item, 'id', 'noiDungID'), title: value(item, 'title', 'tieuDe'), genre: value(item.phim, 'theLoai') || value(item, 'genre', 'theLoai'), rating: value(item.phim, 'diemDanhGiaTB') || value(item, 'rating', 'diemDanhGiaTB'), poster: value(item, 'poster', 'hinhAnh'), description: value(item, 'description', 'moTa'), raw: item })
const normalizeMusic = (item) => ({ valence: item.valence, arousal: item.arousal, moodSource: item.nhac?.moodSource, id: value(item, 'id', 'noiDungID'), title: value(item, 'title', 'tieuDe'), artist: value(item.nhac, 'tenNgheSi') || value(item, 'artist', 'tenNgheSi') || 'Not available', genre: value(item.nhac, 'genre') || value(item, 'genre') || 'Not provided', cover: value(item, 'cover', 'hinhAnh'), duration: secondsToDuration(value(item.nhac, 'duration') || value(item, 'duration')), raw: item })
const moviePayload = (item) => ({ Valence: Number(item.valence), Arousal: Number(item.arousal), MoodSource: item.moodSource || "manual", IMDBID: item.imdbId || null, NoiDungID: item.id, TieuDe: item.title, HinhAnh: item.poster || null, MoTa: item.description || null, TheLoai: item.genre || null, DiemDanhGiaTB: item.rating ? Number(item.rating) : null })
const musicPayload = (item) => ({ Valence: Number(item.valence), Arousal: Number(item.arousal), MoodSource: item.moodSource || "manual", NoiDungID: item.id, TieuDe: item.title, TenNgheSi: item.artist || null, HinhAnh: item.cover || null, Genre: item.genre || null, Duration: durationToSeconds(item.duration) })
const durationToSeconds = (duration = '') => { const parts = String(duration).split(':').map(Number); return parts.length === 2 ? parts[0] * 60 + parts[1] : Number(duration) || null }
const secondsToDuration = (seconds) => { if (seconds === '' || seconds === null || seconds === undefined) return ''; const total = Number(seconds); return Number.isFinite(total) ? `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}` : String(seconds) }
const showError = (message) => { let toast = document.querySelector('.error-toast'); if (!toast) { toast = document.createElement('div'); toast.className = 'error-toast'; document.body.appendChild(toast) } toast.textContent = message; window.clearTimeout(showError.timer); showError.timer = window.setTimeout(() => toast.remove(), 6000) }

function render() {
  stopMusicPreview()
  if (!sessionUser || !['admin', 'quantrivien'].includes(String(sessionUser.vaiTro).toLowerCase())) {
    renderLogin()
    return
  }

  const current = labels[state.activeTab]
  root.innerHTML = `<div class="app-shell"><aside class="sidebar ${state.sidebarOpen ? 'is-open' : ''}"><div class="brand"><div class="brand-mark">M</div><div><strong>Moodify</strong><span>ADMIN CONSOLE</span></div></div><div class="workspace-label">WORKSPACE</div><nav>${Object.keys(labels).map((tab) => `<button class="nav-item ${state.activeTab === tab ? 'active' : ''}" data-tab="${tab}">${icon(icons[tab])}<span>${labels[tab]}</span></button>`).join('')}</nav><div class="sidebar-bottom"><div class="workspace-label">SYSTEM</div><button class="nav-item">${icon('⚙')}<span>Settings</span></button><div class="admin-card"><div class="avatar">AN</div><div><strong>${escapeHtml(sessionUser.tenDangNhap || sessionUser.email || 'Admin')}</strong><span>Administrator</span></div><span class="admin-chevron">⌄</span></div></div></aside><main class="main-content"><header class="topbar"><button class="mobile-menu" data-action="toggle-menu" aria-label="Open menu">☰</button><div class="breadcrumb"><span>Workspace</span><b>/</b><strong>${current}</strong></div><div class="top-actions"><button class="logout" data-action="logout">Log out</button></div></header><section class="content">${['genres', 'movies', 'music'].includes(state.activeTab) ? management() : '<div id=management-host></div>'}</section></main>${state.modal ? editorModal() : ''}${state.deleteTarget ? deleteModal() : ''}</div>`
  bindEvents()
  if (["genres","movies","music"].includes(state.activeTab)) enhanceList(root.querySelector(".table-panel"), {filterColumn:state.activeTab === "music" ? 2 : 1,filterLabel:"Genre", ...(state.activeTab === "genres" ? {filterTabs:[{value:"Movie",label:"Movies"},{value:"Song",label:"Music"}],selectedFilter:state.genreType,onFilterChange:type=>{state.genreType=type}} : {})})
  mountManagement(root.querySelector('#management-host'), state.activeTab, sessionUser)
}

function openSpotifyImport() {
  if(document.querySelector('.spotify-import-dialog')) return
  stopMusicPreview()
  const dialog = document.createElement('dialog')
  dialog.className = 'spotify-import-dialog'
  dialog.setAttribute('aria-labelledby','spotify-import-title')
  dialog.innerHTML = '<div class="modal-heading"><h2 id="spotify-import-title">Add music from Spotify</h2><button type="button" class="close-button" aria-label="Close">×</button></div><div id="spotify-import-host"></div>'
  document.body.append(dialog)
  dialog.querySelector('.close-button').onclick = () => dialog.close()
  dialog.addEventListener('close', () => {
    stopMusicPreview()
    dialog.remove()
    if(sessionUser) { render(); root.querySelector('[data-action="add"]')?.focus() }
  }, {once:true})
  dialog.showModal()
  mountSpotify(dialog.querySelector('#spotify-import-host'), async () => {
    state.data.music = (await request('/Music')).map(normalizeMusic)
    if(!dialog.isConnected && sessionUser) render()
  })
}

function renderLogin() {
  document.querySelector('.spotify-import-dialog')?.close()
  root.innerHTML = `<main class="login-shell"><section class="login-card"><div class="brand-mark">M</div><p class="eyebrow">MOODIFY / ADMIN</p><h1>Admin login</h1><p class="login-subtitle">Sign in with an administrator account to continue.</p><form id="admin-login-form"><label>Email or username<input name="identifier" required autocomplete="username"></label><label>Password<input name="password" type="password" required autocomplete="current-password"></label><p id="login-error" class="login-error" role="alert"></p><button class="primary-button" type="submit">Log in</button></form></section></main>`
  root.querySelector('#admin-login-form').addEventListener('submit', loginAdmin)
}

async function loginAdmin(event) {
  event.preventDefault()
  const form = event.currentTarget
  const error = root.querySelector('#login-error')
  const button = form.querySelector('button')
  button.disabled = true
  error.textContent = ''

  try {
    const response = await fetch(`${API_BASE_URL}/Auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ identifier: form.identifier.value.trim(), matKhau: form.password.value }),
    })
    const data = await response.json()
    if (!response.ok) throw new Error(data.message || 'Login failed.')
    if (!['admin', 'quantrivien'].includes(String(data.user?.vaiTro).toLowerCase())) throw new Error('This account does not have administrator access.')
    sessionStorage.setItem(tokenKey, data.token)
    window.location.reload()
  } catch (loginError) {
    error.textContent = loginError.message
    button.disabled = false
  }
}

function management() {
  for (const genre of state.data.genres) {
    genre.count = [...state.data.movies, ...state.data.music].filter(item =>
      String(item.genre).split(',').some(name => name.trim().toLowerCase() === genre.name.toLowerCase())
    ).length
  }
  const items = state.data[state.activeTab]
  const actionText = state.activeTab === 'genres' ? 'genre' : state.activeTab === 'movies' ? 'movie' : 'song'
  return `<div class="page-intro"><div><p class="eyebrow">LIBRARY / ${state.activeTab.toUpperCase()}</p><h1>${labels[state.activeTab]}</h1><p class="subtitle">Manage the content available to Moodify users.</p></div><button class="primary-button" data-action="add"><b>＋</b> Add ${actionText}</button></div><div class="table-panel"><div class="table-meta"><span><strong>${items.length}</strong> results</span><div class="table-filter">Recently updated⌄</div></div><div class="table-scroll">${tableFor(items)}</div><div class="pagination"><span>Showing 1 - ${items.length} of ${items.length}</span><div><button disabled>←</button><button class="current-page">1</button><button disabled>→</button></div></div></div>`
}

function tableFor(items) {
  if (state.activeTab === 'genres') return `<table><thead><tr><th>Genre name</th><th>Applies to</th><th>Default Valence / Arousal</th><th>Content count</th><th>Status</th><th></th></tr></thead><tbody>${items.map((item) => `<tr data-updated="${escapeHtml(item.raw?.updatedAt || '')}"><td><div class="title-cell"><span class="genre-dot">✦</span><strong>${escapeHtml(item.name)}</strong></div></td><td class="muted-cell">${escapeHtml(item.contentType)}</td><td>${escapeHtml(item.defaultValence ?? 'Unset')} / ${escapeHtml(item.defaultArousal ?? 'Unset')}</td><td>${item.count} items</td><td><span class="status active-status">Active</span></td>${actions(item)}</tr>`).join('')}</tbody></table>`
  if (state.activeTab === 'movies') return `<table><thead><tr><th>Movies</th><th>Genres</th><th>Rating</th><th>Valence / Arousal</th><th>Status</th><th></th></tr></thead><tbody>${items.map((item) => `<tr data-updated="${escapeHtml(item.raw?.updatedAt || '')}"><td><div class="media-cell"><img src="${escapeHtml(item.poster)}" alt=""><strong>${escapeHtml(item.title)}</strong></div></td><td><span class="badge">${escapeHtml(item.genre)}</span></td><td><span class="rating">★ ${escapeHtml(item.rating)}</span></td><td><span class="match">${item.raw?.moodAvailable === false ? '-' : escapeHtml(item.raw?.valence ?? 0)} / ${item.raw?.moodAvailable === false ? '-' : escapeHtml(item.raw?.arousal ?? 0)}</span></td><td><span class="status active-status">${item.raw?.metadataStatus === 'unavailable' ? 'Source unavailable' : 'In library'}</span></td>${actions(item)}</tr>`).join('')}</tbody></table>`
  return `<table><thead><tr><th>Song</th><th>Artist</th><th>Genres</th><th>Duration</th><th>Status</th><th></th></tr></thead><tbody>${items.map((item) => `<tr data-updated="${escapeHtml(item.raw?.updatedAt || '')}"><td><div class="media-cell"><img src="${escapeHtml(item.cover)}" alt=""><strong>${escapeHtml(item.title)}</strong></div></td><td class="muted-cell">${escapeHtml(item.artist)}</td><td><span class="badge">${escapeHtml(item.genre)}</span></td><td>${escapeHtml(item.duration)}</td><td><span class="status active-status">Active</span></td>${actions(item)}</tr>`).join('')}</tbody></table>`
}
function actions(item) {
  const external=item.raw?.isExternalReference
  const edit=external ? (state.activeTab==='movies' && item.imdbId ? '<a href="https://www.imdb.com/title/'+escapeHtml(item.imdbId)+'/" target="_blank" rel="noopener noreferrer" aria-label="View on IMDb">&#8599;</a>' : '') : '<button data-action="edit" data-id="'+escapeHtml(item.id)+'" aria-label="Edit">&#9998;</button>'
  return '<td><div class="row-actions">'+(state.activeTab==='music'?'<button data-music-info="'+escapeHtml(item.id)+'" aria-label="Details and preview">&#9654;</button>':'')+edit+'<button class="delete-action" data-action="delete" data-id="'+escapeHtml(item.id)+'" aria-label="Delete">&#9003;</button></div></td>'
}

function editorModal() {
  const item = state.modal.item || {}; const type = state.modal.type; const imageKey = type === 'movies' ? 'poster' : 'cover'; const title = type === 'genres' ? 'genre' : type === 'movies' ? 'movie' : 'song'; const field = (key, label, placeholder, required = true) => `<label class="field"><span>${label}</span><input ${required ? 'required' : ''} name="${key}" value="${escapeHtml(item[key] ?? '')}" placeholder="${placeholder}"></label>`
  let form = type === 'genres' ? field('name', 'Genre name', 'Example: Horror') : field('title', type === 'movies' ? 'Movie title' : 'Song title', 'Enter a title') + `<div class="form-row">${field(type === 'movies' ? 'rating' : 'artist', type === 'movies' ? 'Rating' : 'Artist', type === 'movies' ? '8.5' : 'Artist name', type !== 'movies')}<label class="field"><span>Genres</span><select required name="genre"><option value="">Select a genre</option>${item.genre && !state.data.genres.some(g => g.name === item.genre) ? `<option selected>${escapeHtml(item.genre)}</option>` : ''}${state.data.genres.filter(g => g.contentType === 'Both' || g.contentType === (type === 'movies' ? 'Movie' : 'Song')).map((genre) => `<option ${item.genre === genre.name ? 'selected' : ''}>${escapeHtml(genre.name)}</option>`).join('')}</select></label></div>` + field(imageKey, type === 'movies' ? 'URL Poster' : 'Cover image URL', 'https://...', false) + (type === 'movies' ? field('description', 'Description', 'Movie synopsis', false) : field('duration', 'Duration (minutes:seconds)', '03:30'))
  if (type === 'movies') form = `<section class="panel"><label class="field"><span>OMDb lookup: original title or IMDb ID</span><input data-omdb-query maxlength="200" placeholder="Example: Guardians of the Galaxy Vol. 2 / tt3896198"></label><label class="field"><span>Release year (optional)</span><input data-omdb-year type="number" min="1800" max="2100" placeholder="2017"></label><button type="button" class="secondary-button" data-omdb>Autofill from OMDb</button><p data-omdb-status role="status"></p></section>` + form + field('imdbId', 'IMDb ID', 'tt3896198', false)
  if (type === 'movies' || type === 'music') form += `<section class="panel"><div class="form-row"><label class="field"><span>Valence: negative (-1) → positive (1)</span><input required name="valence" type="number" min="-1" max="1" step="0.0001" value="${escapeHtml(item.valence ?? '')}"></label><label class="field"><span>Arousal: calm (-1) → energetic (1)</span><input required name="arousal" type="number" min="-1" max="1" step="0.0001" value="${escapeHtml(item.arousal ?? '')}"></label></div><input type="hidden" name="moodSource" value="${escapeHtml(item.moodSource || '')}"><button type="button" class="secondary-button" data-mood-suggest>Suggest from genre</button><p data-mood-status role="status"></p></section>`
  if (type === 'genres') form += '<label class="field"><span>Applies to</span><select name="contentType">'+['Both','Movie','Song'].map(t=>'<option '+((item.contentType||state.genreType)===t?'selected':'')+'>'+t+'</option>').join('')+'</select></label><div class="form-row">'+['defaultValence','defaultArousal'].map((key,i)=>'<label class="field"><span>'+['Default Valence','Default Arousal'][i]+'</span><input name="'+key+'" type="number" min="-1" max="1" step="0.0001" value="'+escapeHtml(item[key]??'')+'"></label>').join('')+'</div><p>Set both values, or leave both empty. Existing content keeps its saved values.</p>'
  return `<div class="modal-backdrop"><div class="editor-modal"><div class="modal-heading"><div><p class="eyebrow">${state.modal.item ? 'EDIT ITEM' : 'NEW ITEM'}</p><h2>${state.modal.item ? 'Edit' : 'Add'} ${title}</h2></div><button class="close-button" data-action="close">×</button></div><form id="editor-form" data-type="${type}">${form}</form><div class="modal-actions"><button class="secondary-button" data-action="close">Cancel</button><button class="primary-button" data-action="save">${state.modal.item ? 'Save changes' : 'Add to library'}</button></div></div></div>`
}
function deleteModal() { return `<div class="modal-backdrop"><div class="confirm-modal"><button class="close-button" data-action="close">×</button><div class="danger-icon">⌫</div><h2>Delete this item?</h2><p>You are about to delete <strong>${escapeHtml(state.deleteTarget.label)}</strong>. This action cannot be undone.</p><div class="modal-actions"><button class="secondary-button" data-action="close">Cancel</button><button class="danger-button" data-action="confirm-delete">Delete now</button></div></div></div>` }

function bindEvents() {
  fillMissingImages(root)
  bindOmdb(root.querySelector('#editor-form'))
  bindMovieMood(root.querySelector('#editor-form'))
  root.querySelectorAll('[data-music-info]').forEach(button => button.onclick = () => openMusicDetails(button.dataset.musicInfo).catch(e => showError(e.message)))
  root.querySelectorAll('[data-tab]').forEach((button) => button.addEventListener('click', () => { state.activeTab = button.dataset.tab; state.query = ''; state.sidebarOpen = false; render() }))
  root.querySelector('[data-action="toggle-menu"]')?.addEventListener('click', () => { state.sidebarOpen = !state.sidebarOpen; render() })
  root.querySelector('[data-action="add"]')?.addEventListener('click', () => { if(state.activeTab === 'music') { openSpotifyImport(); return } state.modal = { type: state.activeTab }; render() })
  root.querySelectorAll('[data-action="edit"]').forEach((button) => button.addEventListener('click', () => {
    const item = state.data[state.activeTab].find((entry) => String(entry.id) === String(button.dataset.id))
    if (!item) { showError('The item to edit was not found.'); return }
    state.modal = { type: state.activeTab, item }; render()
  }))
  root.querySelectorAll('[data-action="delete"]').forEach((button) => button.addEventListener('click', () => {
    const item = state.data[state.activeTab].find((entry) => String(entry.id) === String(button.dataset.id))
    if (!item) { showError('The item to delete was not found.'); return }
    state.deleteTarget = { type: state.activeTab, id: item.id, label: item.title || item.name }; render()
  }))
  root.querySelectorAll('[data-action="close"]').forEach((button) => button.addEventListener('click', () => { state.modal = null; state.deleteTarget = null; render() }))
  root.querySelector('[data-action="save"]')?.addEventListener('click', saveForm)
  root.querySelector('#editor-form')?.addEventListener('submit', event => { event.preventDefault(); saveForm() })
  root.querySelector('[data-action="confirm-delete"]')?.addEventListener('click', confirmDelete)
  root.querySelector('[data-action="logout"]')?.addEventListener('click', () => { sessionStorage.removeItem(tokenKey); localStorage.removeItem('moodify_admin_user'); window.location.reload() })
}

async function saveForm() {
  const form = root.querySelector('#editor-form'); if (!form.reportValidity()) return
  const type = form.dataset.type; const values = Object.fromEntries(new FormData(form)); const existing = state.modal.item; const item = { ...values, id: existing?.id ?? (type === 'genres' ? undefined : crypto.randomUUID()) }
  if (type === 'genres') item.count = Number(item.count || 0)

  if (type === 'music') item.duration = item.duration || '03:30'
  const payload = type === 'movies' ? moviePayload(item) : type === 'music' ? musicPayload(item) : { TenTheLoai: item.name, ContentType: item.contentType, DefaultValence: item.defaultValence === '' ? null : Number(item.defaultValence), DefaultArousal: item.defaultArousal === '' ? null : Number(item.defaultArousal), ClearMoodDefaults: item.defaultValence === '' && item.defaultArousal === '' }
  try {
    const saved = await saveResource(type, type === 'genres' ? '/Genres' : type === 'movies' ? '/Movies' : '/Music', payload, existing?.id)
    const savedItem = saved
      ? type === 'genres' ? normalizeGenre(saved) : type === 'movies' ? normalizeMovie(saved) : normalizeMusic(saved)
      : item
    state.data[type] = existing
      ? state.data[type].map((entry) => entry.id === existing.id ? savedItem : entry)
      : [...state.data[type], savedItem]
    state.data.genres = (await request('/Genres')).map(normalizeGenre)
    if (type === 'genres') {
      state.data.movies = (await request('/Movies')).map(normalizeMovie)
      state.data.music = (await request('/Music')).map(normalizeMusic)
    }
    state.modal = null; render()
  } catch (error) {
    showError(error.message || 'Unable to save data.')
  }
}
async function confirmDelete() {
  const target = state.deleteTarget
  try {
    await deleteResource(target.type, target.type === 'genres' ? '/Genres' : target.type === 'movies' ? '/Movies' : '/Music', target.id)
    state.data[target.type] = state.data[target.type].filter((item) => item.id !== target.id); state.deleteTarget = null; render()
  } catch (error) {
    showError(error.message || 'Unable to delete data.')
  }
}

async function initialize() {
  root.innerHTML = '<p role="status">Checking your session…</p>'
  try {
    await resumeAdminSession()
    if (!sessionStorage.getItem(tokenKey)) { render(); return }
    sessionUser = await request('/Auth/me')
    if (!['admin', 'quantrivien'].includes(String(sessionUser.vaiTro).toLowerCase())) {
      sessionStorage.removeItem(tokenKey); sessionUser = null; render(); return
    }
    const [genres, movies, music] = await Promise.all([
      getCollection('genres', ['/Genres']), getCollection('movies', ['/Movies']), getCollection('music', ['/Music'])
    ])
    state.data = { genres: genres.map(normalizeGenre), movies: movies.map(normalizeMovie), music: music.map(normalizeMusic) }
  } catch (error) { showError(error.message || 'Unable to load data from the backend.') }
  render()
}
window.addEventListener('moodify-session-expired', () => { sessionUser = null; render() })
initialize()
