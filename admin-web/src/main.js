import { API_BASE_URL, deleteResource, getCollection, saveResource } from './api.js'

const seeds = {
  genres: [
    { id: 'g1', name: 'Tâm lý', description: 'Những câu chuyện chạm đến cảm xúc', count: 24 },
    { id: 'g2', name: 'Khoa học viễn tưởng', description: 'Khám phá thế giới ngoài trí tưởng tượng', count: 18 },
    { id: 'g3', name: 'Hài hước', description: 'Nụ cười cho những ngày nhiều năng lượng', count: 31 },
    { id: 'g4', name: 'Phiêu lưu', description: 'Hành trình không thể đoán trước', count: 16 },
  ],
  movies: [
    { id: 'm1', title: 'Dune: Part Two', genre: 'Khoa học viễn tưởng', rating: '8.8', match: 96, poster: 'https://images.unsplash.com/photo-1534447677768-be436bb09401?w=160&q=85', description: 'Paul Atreides hợp nhất với Chani và người Fremen.' },
    { id: 'm2', title: 'Past Lives', genre: 'Tâm lý', rating: '8.1', match: 91, poster: 'https://images.unsplash.com/photo-1485846234645-a62644f84728?w=160&q=85', description: 'Một câu chuyện dịu dàng về những người đi qua đời nhau.' },
    { id: 'm3', title: 'The Grand Budapest Hotel', genre: 'Hài hước', rating: '8.1', match: 88, poster: 'https://images.unsplash.com/photo-1500534623283-312aade485b7?w=160&q=85', description: 'Cuộc phiêu lưu kỳ quặc trong khách sạn thanh lịch.' },
    { id: 'm4', title: 'Interstellar', genre: 'Phiêu lưu', rating: '8.7', match: 84, poster: 'https://images.unsplash.com/photo-1446776811953-b23d57bd21aa?w=160&q=85', description: 'Một chuyến đi vượt qua không gian để cứu tương lai.' },
  ],
  music: [
    { id: 't1', title: 'Midnight City', artist: 'M83', genre: 'Synthwave', cover: 'https://images.unsplash.com/photo-1516280440614-37939bbacd81?w=160&q=85', duration: '04:03' },
    { id: 't2', title: 'Space Song', artist: 'Beach House', genre: 'Dream Pop', cover: 'https://images.unsplash.com/photo-1519608487953-e999c86e7455?w=160&q=85', duration: '05:20' },
    { id: 't3', title: 'Intro', artist: 'The xx', genre: 'Indie', cover: 'https://images.unsplash.com/photo-1524368535928-5b5e00ddc76b?w=160&q=85', duration: '02:07' },
    { id: 't4', title: 'Sunset Lover', artist: 'Petit Biscuit', genre: 'Electronic', cover: 'https://images.unsplash.com/photo-1492684223066-81342ee5ff30?w=160&q=85', duration: '03:58' },
  ],
}

const labels = { overview: 'Tổng quan', genres: 'Thể loại', movies: 'Phim', music: 'Nhạc' }
const icons = { overview: '▦', genres: '◇', movies: '▣', music: '♫' }
const state = { activeTab: 'overview', query: '', sidebarOpen: false, modal: null, deleteTarget: null, data: structuredClone(seeds) }
const root = document.querySelector('#root')
let sessionUser = JSON.parse(localStorage.getItem('moodify_admin_user') || 'null')
const adminUserParam = new URLSearchParams(window.location.search).get('adminUser')
if (adminUserParam) {
  try {
    sessionUser = JSON.parse(adminUserParam)
    localStorage.setItem('moodify_admin_user', JSON.stringify(sessionUser))
    window.history.replaceState({}, document.title, window.location.pathname)
  } catch {
    localStorage.removeItem('moodify_admin_user')
  }
}
const escapeHtml = (value = '') => String(value).replace(/[&<>'"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#039;', '"': '&quot;' }[char]))
const icon = (name) => `<span class="icon-glyph" aria-hidden="true">${name}</span>`
const value = (item, ...keys) => keys.map((key) => item?.[key]).find((entry) => entry !== undefined && entry !== null) ?? ''
const normalizeGenre = (item) => ({ id: value(item, 'id', 'theLoaiID'), name: value(item, 'name', 'tenTheLoai'), description: 'Thể loại nội dung Moodify', count: 0, raw: item })
const normalizeMovie = (item) => ({ id: value(item, 'id', 'noiDungID'), title: value(item, 'title', 'tieuDe'), genre: value(item.phim, 'theLoai') || value(item, 'genre', 'theLoai'), rating: value(item.phim, 'diemDanhGiaTB') || value(item, 'rating', 'diemDanhGiaTB'), match: Number(value(item, 'match')) || 90, poster: value(item, 'poster', 'hinhAnh'), description: value(item, 'description', 'moTa'), raw: item })
const normalizeMusic = (item) => ({ id: value(item, 'id', 'noiDungID'), title: value(item, 'title', 'tieuDe'), artist: value(item.nhac, 'tenNgheSi') || value(item, 'artist', 'tenNgheSi') || 'Chưa cập nhật', genre: value(item.nhac, 'genre') || value(item, 'genre') || 'Song', cover: value(item, 'cover', 'hinhAnh'), duration: secondsToDuration(value(item.nhac, 'duration') || value(item, 'duration')), raw: item })
const moviePayload = (item) => ({ NoiDungID: item.id, TieuDe: item.title, HinhAnh: item.poster || null, MoTa: item.description || null, TheLoai: item.genre || null, DiemDanhGiaTB: item.rating ? Number(item.rating) : null })
const musicPayload = (item) => ({ NoiDungID: item.id, TieuDe: item.title, TenNgheSi: item.artist || null, HinhAnh: item.cover || null, Genre: item.genre || null, Duration: durationToSeconds(item.duration) })
const durationToSeconds = (duration = '') => { const parts = String(duration).split(':').map(Number); return parts.length === 2 ? parts[0] * 60 + parts[1] : Number(duration) || null }
const secondsToDuration = (seconds) => { if (seconds === '' || seconds === null || seconds === undefined) return ''; const total = Number(seconds); return Number.isFinite(total) ? `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}` : String(seconds) }
const showError = (message) => { let toast = document.querySelector('.error-toast'); if (!toast) { toast = document.createElement('div'); toast.className = 'error-toast'; document.body.appendChild(toast) } toast.textContent = message; window.clearTimeout(showError.timer); showError.timer = window.setTimeout(() => toast.remove(), 6000) }

function render() {
  if (!sessionUser || !['admin', 'quantrivien'].includes(String(sessionUser.vaiTro).toLowerCase())) {
    renderLogin()
    return
  }

  const current = labels[state.activeTab]
  root.innerHTML = `<div class="app-shell"><aside class="sidebar ${state.sidebarOpen ? 'is-open' : ''}"><div class="brand"><div class="brand-mark">M</div><div><strong>Moodify</strong><span>ADMIN CONSOLE</span></div></div><div class="workspace-label">WORKSPACE</div><nav>${Object.keys(labels).map((tab) => `<button class="nav-item ${state.activeTab === tab ? 'active' : ''}" data-tab="${tab}">${icon(icons[tab])}<span>${labels[tab]}</span>${tab === 'genres' ? '<em>4</em>' : ''}</button>`).join('')}</nav><div class="sidebar-bottom"><div class="workspace-label">SYSTEM</div><button class="nav-item">${icon('⚙')}<span>Cài đặt</span></button><div class="admin-card"><div class="avatar">AN</div><div><strong>${escapeHtml(sessionUser.tenDangNhap || sessionUser.email || 'Admin')}</strong><span>Administrator</span></div><span class="admin-chevron">⌄</span></div></div></aside><main class="main-content"><header class="topbar"><button class="mobile-menu" data-action="toggle-menu" aria-label="Mở menu">☰</button><div class="breadcrumb"><span>Workspace</span><b>/</b><strong>${current}</strong></div><div class="top-actions"><label class="search">⌕<input id="search-input" value="${escapeHtml(state.query)}" placeholder="Tìm kiếm..." /><kbd>⌘ K</kbd></label><button class="logout" data-action="logout">Đăng xuất</button></div></header><section class="content">${state.activeTab === 'overview' ? overview() : management()}</section></main>${state.modal ? editorModal() : ''}${state.deleteTarget ? deleteModal() : ''}</div>`
  bindEvents()
}

function renderLogin() {
  root.innerHTML = `<main class="login-shell"><section class="login-card"><div class="brand-mark">M</div><p class="eyebrow">MOODIFY / ADMIN</p><h1>Đăng nhập quản trị</h1><p class="login-subtitle">Sử dụng tài khoản có quyền Admin để tiếp tục.</p><form id="admin-login-form"><label>Email hoặc tên đăng nhập<input name="identifier" required autocomplete="username"></label><label>Mật khẩu<input name="password" type="password" required autocomplete="current-password"></label><p id="login-error" class="login-error" role="alert"></p><button class="primary-button" type="submit">Đăng nhập</button></form></section></main>`
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
    if (!response.ok) throw new Error(data.message || 'Đăng nhập thất bại.')
    if (!['admin', 'quantrivien'].includes(String(data.user?.vaiTro).toLowerCase())) throw new Error('Tài khoản này không có quyền quản trị.')
    localStorage.setItem('moodify_admin_user', JSON.stringify(data.user))
    window.location.reload()
  } catch (loginError) {
    error.textContent = loginError.message
    button.disabled = false
  }
}

function overview() {
  const { genres, movies, music } = state.data
  return `<div class="page-intro"><div><p class="eyebrow">TUESDAY, 15 SEPTEMBER 2026</p><h1>Chào buổi sáng, Alex <span>✦</span></h1><p class="subtitle">Đây là những gì đang diễn ra trong thư viện Moodify hôm nay.</p></div><button class="ghost-button">▥ Báo cáo tháng</button></div><div class="stats-grid">${stat('▣', 'Tổng nội dung', movies.length + music.length, '12.4%', 'purple')}${stat('▣', 'Phim', movies.length, '8.2%', 'blue')}${stat('♫', 'Bài hát', music.length, '16.8%', 'green')}${stat('◇', 'Thể loại', genres.length, '2.1%', 'orange')}</div><div class="overview-grid"><section class="panel"><div class="panel-heading"><div><h2>Hoạt động gần đây</h2><p>Cập nhật mới nhất từ hệ thống</p></div><button class="more-button">Xem tất cả ⌄</button></div><div class="activity-list">${activity('▣', 'purple', 'Đã thêm phim mới', 'Dune: Part Two', '12 phút trước')}${activity('♫', 'green', 'Cập nhật bài hát', 'Midnight City · M83', '2 giờ trước')}${activity('◇', 'orange', 'Đã tạo thể loại', 'Phiêu lưu', 'Hôm qua')}</div></section><section class="panel"><div class="panel-heading"><div><h2>Truy cập nhanh</h2><p>Quản lý nội dung ngay</p></div></div>${quick('movies', '▣', 'purple', 'Thêm phim mới', 'Đăng tải nội dung điện ảnh')}${quick('music', '♫', 'green', 'Thêm bài hát', 'Cập nhật thư viện âm nhạc')}${quick('genres', '◇', 'orange', 'Quản lý thể loại', 'Phân loại nội dung')}</section></div>`
}

function stat(symbol, label, value, delta, tone) { return `<div class="stat-card"><div class="stat-icon ${tone}">${symbol}</div><div class="stat-copy"><span>${label}</span><strong>${value}</strong><small><b>↗ ${delta}</b> so với tháng trước</small></div></div>` }
function activity(symbol, tone, title, detail, time) { return `<div class="activity-row"><div class="activity-icon ${tone}">${symbol}</div><div><strong>${title}</strong><span>${detail}</span></div><time>${time}</time></div>` }
function quick(tab, symbol, tone, title, detail) { return `<button class="quick-item" data-tab="${tab}"><span class="quick-icon ${tone}">${symbol}</span><span><strong>${title}</strong><small>${detail}</small></span><b>＋</b></button>` }

function management() {
  const items = state.data[state.activeTab].filter((item) => JSON.stringify(item).toLowerCase().includes(state.query.toLowerCase()))
  const actionText = state.activeTab === 'genres' ? 'thể loại' : state.activeTab === 'movies' ? 'phim mới' : 'bài hát'
  return `<div class="page-intro"><div><p class="eyebrow">LIBRARY / ${state.activeTab.toUpperCase()}</p><h1>${labels[state.activeTab]}</h1><p class="subtitle">Quản lý và sắp xếp nội dung trải nghiệm của người dùng Moodify.</p></div><button class="primary-button" data-action="add"><b>＋</b> Thêm ${actionText}</button></div><div class="table-panel"><div class="table-meta"><span><strong>${items.length}</strong> kết quả</span><div class="table-filter">Mới cập nhật⌄</div></div><div class="table-scroll">${tableFor(items)}</div><div class="pagination"><span>Hiển thị 1 - ${items.length} trong ${items.length}</span><div><button disabled>←</button><button class="current-page">1</button><button disabled>→</button></div></div></div>`
}

function tableFor(items) {
  if (state.activeTab === 'genres') return `<table><thead><tr><th>Tên thể loại</th><th>Mô tả</th><th>Số nội dung</th><th>Trạng thái</th><th></th></tr></thead><tbody>${items.map((item) => `<tr><td><div class="title-cell"><span class="genre-dot">✦</span><strong>${escapeHtml(item.name)}</strong></div></td><td class="muted-cell">${escapeHtml(item.description)}</td><td>${item.count} nội dung</td><td><span class="status active-status">Đang hoạt động</span></td>${actions(item)}</tr>`).join('')}</tbody></table>`
  if (state.activeTab === 'movies') return `<table><thead><tr><th>Phim</th><th>Thể loại</th><th>Đánh giá</th><th>Match</th><th>Trạng thái</th><th></th></tr></thead><tbody>${items.map((item) => `<tr><td><div class="media-cell"><img src="${escapeHtml(item.poster)}" alt=""><strong>${escapeHtml(item.title)}</strong></div></td><td><span class="badge">${escapeHtml(item.genre)}</span></td><td><span class="rating">★ ${item.rating}</span></td><td><span class="match">${item.match}%</span></td><td><span class="status active-status">Đang chiếu</span></td>${actions(item)}</tr>`).join('')}</tbody></table>`
  return `<table><thead><tr><th>Bài hát</th><th>Nghệ sĩ</th><th>Thể loại</th><th>Thời lượng</th><th>Trạng thái</th><th></th></tr></thead><tbody>${items.map((item) => `<tr><td><div class="media-cell"><img src="${escapeHtml(item.cover)}" alt=""><strong>${escapeHtml(item.title)}</strong></div></td><td class="muted-cell">${escapeHtml(item.artist)}</td><td><span class="badge">${escapeHtml(item.genre)}</span></td><td>${escapeHtml(item.duration)}</td><td><span class="status active-status">Đang hoạt động</span></td>${actions(item)}</tr>`).join('')}</tbody></table>`
}
function actions(item) { return `<td><div class="row-actions"><button data-action="edit" data-id="${item.id}" aria-label="Chỉnh sửa">✎</button><button class="delete-action" data-action="delete" data-id="${item.id}" aria-label="Xóa">⌫</button></div></td>` }

function editorModal() {
  const item = state.modal.item || {}; const type = state.modal.type; const imageKey = type === 'movies' ? 'poster' : 'cover'; const title = type === 'genres' ? 'thể loại' : type === 'movies' ? 'phim' : 'bài hát'; const field = (key, label, placeholder, required = true) => `<label class="field"><span>${label}</span><input ${required ? 'required' : ''} name="${key}" value="${escapeHtml(item[key] ?? '')}" placeholder="${placeholder}"></label>`
  let form = type === 'genres' ? field('name', 'Tên thể loại', 'Ví dụ: Kinh dị') + field('description', 'Mô tả', 'Mô tả ngắn về thể loại') : field('title', type === 'movies' ? 'Tên phim' : 'Tên bài hát', 'Nhập tên nội dung') + `<div class="form-row">${field(type === 'movies' ? 'rating' : 'artist', type === 'movies' ? 'Đánh giá' : 'Nghệ sĩ', type === 'movies' ? '8.5' : 'Tên nghệ sĩ')}<label class="field"><span>Thể loại</span><select required name="genre"><option value="">Chọn thể loại</option>${state.data.genres.map((genre) => `<option ${item.genre === genre.name ? 'selected' : ''}>${escapeHtml(genre.name)}</option>`).join('')}</select></label></div>` + field(imageKey, type === 'movies' ? 'URL Poster' : 'URL ảnh bìa', 'https://...') + (type === 'movies' ? field('description', 'Mô tả', 'Tóm tắt nội dung phim') : '')
  return `<div class="modal-backdrop"><div class="editor-modal"><div class="modal-heading"><div><p class="eyebrow">${state.modal.item ? 'EDIT ITEM' : 'NEW ITEM'}</p><h2>${state.modal.item ? 'Chỉnh sửa' : 'Thêm'} ${title}</h2></div><button class="close-button" data-action="close">×</button></div><form id="editor-form" data-type="${type}">${form}</form><div class="modal-actions"><button class="secondary-button" data-action="close">Hủy</button><button class="primary-button" data-action="save">${state.modal.item ? 'Lưu thay đổi' : 'Thêm vào thư viện'}</button></div></div></div>`
}
function deleteModal() { return `<div class="modal-backdrop"><div class="confirm-modal"><button class="close-button" data-action="close">×</button><div class="danger-icon">⌫</div><h2>Xóa nội dung này?</h2><p>Bạn sắp xóa <strong>${escapeHtml(state.deleteTarget.label)}</strong>. Hành động này không thể hoàn tác.</p><div class="modal-actions"><button class="secondary-button" data-action="close">Hủy</button><button class="danger-button" data-action="confirm-delete">Xóa ngay</button></div></div></div>` }

function bindEvents() {
  root.querySelectorAll('[data-tab]').forEach((button) => button.addEventListener('click', () => { state.activeTab = button.dataset.tab; state.query = ''; state.sidebarOpen = false; render() }))
  root.querySelector('[data-action="toggle-menu"]')?.addEventListener('click', () => { state.sidebarOpen = !state.sidebarOpen; render() })
  root.querySelector('#search-input')?.addEventListener('input', (event) => { state.query = event.target.value; render(); const input = root.querySelector('#search-input'); input.focus(); input.setSelectionRange(state.query.length, state.query.length) })
  root.querySelector('[data-action="add"]')?.addEventListener('click', () => { state.modal = { type: state.activeTab }; render() })
  root.querySelectorAll('[data-action="edit"]').forEach((button) => button.addEventListener('click', () => {
    const item = state.data[state.activeTab].find((entry) => String(entry.id) === String(button.dataset.id))
    if (!item) { showError('Không tìm thấy dữ liệu cần chỉnh sửa.'); return }
    state.modal = { type: state.activeTab, item }; render()
  }))
  root.querySelectorAll('[data-action="delete"]').forEach((button) => button.addEventListener('click', () => {
    const item = state.data[state.activeTab].find((entry) => String(entry.id) === String(button.dataset.id))
    if (!item) { showError('Không tìm thấy dữ liệu cần xóa.'); return }
    state.deleteTarget = { type: state.activeTab, id: item.id, label: item.title || item.name }; render()
  }))
  root.querySelectorAll('[data-action="close"]').forEach((button) => button.addEventListener('click', () => { state.modal = null; state.deleteTarget = null; render() }))
  root.querySelector('[data-action="save"]')?.addEventListener('click', saveForm)
  root.querySelector('[data-action="confirm-delete"]')?.addEventListener('click', confirmDelete)
  root.querySelector('[data-action="logout"]')?.addEventListener('click', () => { localStorage.removeItem('moodify_admin_user'); window.location.reload() })
}

async function saveForm() {
  const form = root.querySelector('#editor-form'); if (!form.reportValidity()) return
  const type = form.dataset.type; const values = Object.fromEntries(new FormData(form)); const existing = state.modal.item; const item = { ...values, id: existing?.id || `${type[0]}${Date.now()}` }
  if (type === 'genres') item.count = Number(item.count || 0)
  if (type === 'movies') { item.match = Number(item.match || 90); item.rating = item.rating || '0.0' }
  if (type === 'music') item.duration = item.duration || '03:30'
  const payload = type === 'movies' ? moviePayload(item) : type === 'music' ? musicPayload(item) : { TenTheLoai: item.name }
  try {
    const saved = await saveResource(type, type === 'genres' ? '/Genres' : type === 'movies' ? '/Movies' : '/Music', payload, existing?.id)
    const savedItem = saved
      ? type === 'genres' ? normalizeGenre(saved) : type === 'movies' ? normalizeMovie(saved) : normalizeMusic(saved)
      : item
    state.data[type] = existing
      ? state.data[type].map((entry) => entry.id === existing.id ? savedItem : entry)
      : [...state.data[type], savedItem]
    state.modal = null; render()
  } catch (error) {
    showError(error.message || 'Không thể lưu dữ liệu.')
  }
}
async function confirmDelete() {
  const target = state.deleteTarget
  try {
    await deleteResource(target.type, target.type === 'genres' ? '/Genres' : target.type === 'movies' ? '/Movies' : '/Music', target.id)
    state.data[target.type] = state.data[target.type].filter((item) => item.id !== target.id); state.deleteTarget = null; render()
  } catch (error) {
    showError(error.message || 'Không thể xóa dữ liệu.')
  }
}

Promise.all([
  getCollection('genres', ['/Genres'], seeds.genres).then((items) => { state.data.genres = items.map(normalizeGenre) }),
  getCollection('movies', ['/Movies', '/Test/contents'], seeds.movies).then((items) => { state.data.movies = items.map(normalizeMovie).filter((item) => !item.raw?.loaiNoiDung || ['movie', 'phim'].includes(item.raw.loaiNoiDung.toLowerCase())) }),
  getCollection('music', ['/Music'], seeds.music).then((items) => items.length ? items : getCollection('music', ['/Test/contents'], seeds.music)).then((items) => { state.data.music = items.map(normalizeMusic).filter((item) => !item.raw?.loaiNoiDung || ['music', 'nhac', 'nhạc', 'song'].includes(item.raw.loaiNoiDung.toLowerCase())) }),
]).catch((error) => showError(error.message || 'Không thể tải dữ liệu từ backend.')).finally(() => render())
