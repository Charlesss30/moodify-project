import { enhanceList, allPages } from './list-controls.js'
import { request } from './api.js'

const esc = (v = '') => String(v ?? '').replace(/[&<>'"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]))
const field = (name, label, value = '', type = 'text', attrs = '') => `<label class="field"><span>${label}</span><input name="${name}" type="${type}" value="${esc(value)}" ${attrs}></label>`
const button = (text) => `<button class="primary-button" type="submit">${text}</button>`
const table = (headers, rows) => `<div class="table-scroll"><table><thead><tr>${headers.map(h => '<th>'+h+'</th>').join('')}</tr></thead><tbody>${rows.join('') || '<tr><td colspan="8">No data yet.</td></tr>'}</tbody></table></div>`
const cell = v => '<td>'+esc(v)+'</td>'
const date = v => new Date(v).toLocaleString('en-US')
const pager = (data, page) => `<div class="pagination"><button data-page="${page-1}" ${page===1?'disabled':''}>Previous</button><span>Page ${page} · ${data.total} results</span><button data-page="${page+1}" ${page*50>=data.total?'disabled':''}>Next</button></div>`

export async function mountManagement(host, tab, user, page = 1, search = '') {
  if (!host) return
  host.innerHTML = '<p role="status">Loading data…</p>'
  const reload = () => mountManagement(host, tab, user, page, search)
  const bindForm = (selector, handler) => host.querySelectorAll(selector).forEach(form => form.addEventListener('submit', async event => {
    event.preventDefault()
    if (!form.reportValidity()) return
    const submit = form.querySelector('[type="submit"]')
    if (submit) submit.disabled = true
    try { await handler(Object.fromEntries(new FormData(form)), form); }
    catch (error) { showError(error.message) }
    finally { if (submit) submit.disabled = false }
  }))
  const showError = message => { const el = host.querySelector('[role="alert"]'); if (el) el.textContent = message }
  const panel = content => { if (host.isConnected) host.innerHTML = '<p role="alert" class="login-error"></p>'+content }
  try {
    if (tab === 'overview') {
      const d = await request('/Dashboard')
      const stats = [['Users',d.users],['Active',d.activeUsers],['Movies',d.movies],['Music',d.music],['Genres',d.genres],['Mood history',d.moodEntries],['Rating',d.ratings],['Average rating',d.averageRating?.toFixed(2) ?? '—']]
      panel(`<div class="page-intro"><div><h1>Hello, ${esc(user.tenDangNhap)}</h1><p>Statistics from current data · ${new Date().toLocaleDateString('en-US')}</p></div><button id="export-report" class="primary-button">Download JSON report</button></div><div class="stats-grid">${stats.map(([label,count]) => `<div class="stat-card"><div class="stat-copy"><span>${label}</span><strong>${count}</strong></div></div>`).join('')}</div><section class="panel"><h2>Mood distribution</h2>${table(['Moods','Count'],d.moods.map(m=>'<tr>'+cell(m.name)+cell(m.count)+'</tr>'))}</section>`)
      host.querySelector('#export-report')?.addEventListener('click', () => {
        const url = URL.createObjectURL(new Blob([JSON.stringify({...d, exportedAt:new Date().toISOString()}, null, 2)], {type:'application/json'}))
        const a=document.createElement('a'); a.href=url; a.download='moodify-report.json'; a.click(); setTimeout(()=>URL.revokeObjectURL(url),1000)
      })
    } else if (tab === 'users') {
      const data = await allPages('/Users')
      panel(`<h1>User management</h1><form id="user-search">${field('search','Search by name or email',search)}${button('Search')}</form>`+table(['Account','Email','Role and status'],data.items.map(u => `<tr data-updated="${esc(u.updatedAt || '')}">${cell(u.tenDangNhap)}${cell(u.email)}<td><form class="access-form" data-id="${u.taiKhoanID}"><select name="vaiTro">${['User','Admin','AIEngineer'].map(r=>`<option ${(r===u.vaiTro || (r==='Admin' && String(u.vaiTro).toLowerCase()==='admin'))?'selected':''}>${r}</option>`).join('')}</select><label><input type="checkbox" name="trangThai" ${u.trangThai?'checked':''}> Active</label><button type="submit" class="secondary-button" ${u.taiKhoanID===user.taiKhoanID?'disabled':''}>Save</button></form></td></tr>`))+pager(data,page))
      bindForm('#user-search', async data => mountManagement(host,tab,user,1,data.search))
      bindForm('.access-form', async (data, form) => {
        await request('/Users/'+form.dataset.id+'/access',{method:'PUT',body:JSON.stringify({vaiTro:data.vaiTro,trangThai:data.trangThai==='on'})}); await reload()
      })
    } else if (tab === 'moods') {
      const moods = await request('/MoodMapping')
      const editForm = m => `<form class="mood-form" data-id="${m.tamTrangID ?? ''}">${field('tenTamTrang','Mood name',m.tenTamTrang,'text','required maxlength="50"')}<div class="form-row">${['minValence','maxValence','minArousal','maxArousal'].map(n=>field(n,n,m[n]??0,'number','required min="-1" max="1" step="0.0001"')).join('')}</div>${button(m.tamTrangID?'Save changes':'Add mood')}${m.tamTrangID?'<button class="secondary-button" type="button" data-remove="'+m.tamTrangID+'">Delete</button>':''}</form>`
      panel('<h1>Mood mapping</h1><p>Valence: negative → positive. Arousal: calm → energetic. Values range from -1 to 1.</p><section class="panel">'+editForm({})+'</section>'+moods.map(m=>'<details class="panel" data-updated="'+esc(m.updatedAt||'')+'"><summary>'+esc(m.tenTamTrang)+'</summary>'+editForm(m)+'</details>').join(''))
      bindForm('.mood-form', async (data,form) => {
        for(const key of ['minValence','maxValence','minArousal','maxArousal']) data[key]=Number(data[key])
        await request('/MoodMapping'+(form.dataset.id?'/'+form.dataset.id:''),{method:form.dataset.id?'PUT':'POST',body:JSON.stringify(data)}); await reload()
      })
      host.querySelectorAll('[data-remove]').forEach(b=>b.addEventListener('click',async()=>{
        if (!window.confirm('Delete this mood?')) return
        try { await request('/MoodMapping/'+b.dataset.remove,{method:'DELETE'}); await reload() } catch(e){showError(e.message)}
      }))
    } else if (tab === 'feedback') {
      const d=await allPages('/Dashboard/feedback')
      panel('<h1>User feedback</h1>'+table(['Users','Content','Stars','Comment','Time'],d.items.map(r=>'<tr data-updated="'+esc(r.thoiGian)+'">'+cell(r.taiKhoanID)+cell(r.noiDungID)+cell(r.soSao)+cell(r.nhanXet)+cell(date(r.thoiGian))+'</tr>'))+pager(d,page))
    } else if (tab === 'recommend') {
      const moods=await request('/MoodMapping')
      panel(`<h1>Test mood recommendations</h1><p>Each request saves a mood history entry for the signed-in account.</p><form id="recommend-form"><label class="field">Moods<select name="tamTrangID" required>${moods.map(m=>`<option value="${m.tamTrangID}">${esc(m.tenTamTrang)}</option>`).join('')}</select></label><select name="loaiNoiDung"><option>Movie</option><option>Music</option></select>${button('Get recommendations')}</form><div id="recommend-results"></div>`)
      bindForm('#recommend-form',async data=>{
        const d=await request('/Recommendation',{method:'POST',body:JSON.stringify({...data,tamTrangID:Number(data.tamTrangID),limit:12})})
        host.querySelector('#recommend-results').innerHTML=table(['Content','Match'],d.items.map(x=>'<tr>'+cell(x.content.tieuDe)+cell(x.match+'%')+'</tr>'))
      })
    } else if (tab === 'history') {
      const d=await allPages('/History')
      panel('<h1>My mood history</h1>'+table(['Moods','Valence','Arousal','Time'],d.items.map(x=>'<tr data-updated="'+esc(x.thoiGian)+'">'+cell(x.tenTamTrang)+cell(x.valence)+cell(x.arousal)+cell(date(x.thoiGian))+'</tr>'))+pager(d,page))
    } else if (tab === 'models') {
      const d=await request('/Models')
      panel('<h1>Model status</h1>'+table(['Component','Version','Status'],d.items.map(x=>'<tr>'+cell(x.name)+cell(x.version)+cell(x.status)+'</tr>'))+'<p>NLP weights and training data are not available yet. Add and validate a model before enabling text inference or retraining.</p>')
    }
    if(tab !== 'overview' && tab !== 'recommend') enhanceList(host, {nodes:tab === 'moods' ? [...host.querySelectorAll('details')] : undefined,filterColumn:tab === 'users' ? 2 : ['feedback','models'].includes(tab) ? 2 : undefined,filterLabel:tab === 'users' ? 'Role' : tab === 'models' ? 'Status' : 'Stars',dateLabel:['history','feedback'].includes(tab)?'Date':'Updated',dateAvailable:tab!=='models'})
    host.querySelectorAll('[data-page]').forEach(b=>b.addEventListener('click',()=>mountManagement(host,tab,user,Number(b.dataset.page),search)))
  } catch (error) {
    panel('<p>Unable to load data.</p><button id="retry" class="primary-button">Retry</button>')
    showError(error.message)
    host.querySelector('#retry')?.addEventListener('click',reload)
  }
}
