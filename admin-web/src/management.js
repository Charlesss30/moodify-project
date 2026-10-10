import { enhanceList, allPages } from './list-controls.js'
import { request } from './api.js'
import { fillMissingImages } from './media.js'

const esc = (v = '') => String(v ?? '').replace(/[&<>'"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]))
const field = (name, label, value = '', type = 'text', attrs = '') => `<label class="field"><span>${label}</span><input name="${name}" type="${type}" value="${esc(value)}" ${attrs}></label>`
const button = (text) => `<button class="primary-button" type="submit">${text}</button>`
const table = (headers, rows) => `<div class="table-scroll"><table><thead><tr>${headers.map(h => '<th>'+h+'</th>').join('')}</tr></thead><tbody>${rows.join('') || '<tr><td colspan="8">No data yet.</td></tr>'}</tbody></table></div>`
const cell = v => '<td>'+esc(v)+'</td>'
const date = v => new Date(v).toLocaleString('en-US')
const pager = (data, page) => `<div class="pagination"><button data-page="${page-1}" ${page===1?'disabled':''}>Previous</button><span>Page ${page} · ${data.total} results</span><button data-page="${page+1}" ${page*50>=data.total?'disabled':''}>Next</button></div>`

export async function mountManagement(host, tab, user, page = 1, search = '', modelSection = '') {
  if (!host) return
  host.innerHTML = '<p role="status">Loading data…</p>'
  const reload = () => mountManagement(host, tab, user, page, search, modelSection)
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
      panel(`<h1>User management</h1><form id="user-search">${field('search','Search by name or email',search)}${button('Search')}</form>`+table(['Account','Email','Role and status'],data.items.map(u => `<tr data-updated="${esc(u.updatedAt || '')}"><td>${esc(u.tenDangNhap)} <button type="button" class="secondary-button" data-user-activity="${u.taiKhoanID}">Activity</button></td>${cell(u.email)}<td><form class="access-form" data-id="${u.taiKhoanID}"><select name="vaiTro">${['User','Admin','AIEngineer'].map(r=>`<option ${(r===u.vaiTro || (r==='Admin' && String(u.vaiTro).toLowerCase()==='admin'))?'selected':''}>${r}</option>`).join('')}</select><label><input type="checkbox" name="trangThai" ${u.trangThai?'checked':''}> Active</label><button type="submit" class="secondary-button" ${u.taiKhoanID===user.taiKhoanID?'disabled':''}>Save</button></form></td></tr>`))+pager(data,page))
      host.querySelectorAll('[data-user-activity]').forEach(b => b.addEventListener('click', () => showUserActivity(b.dataset.userActivity)))
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
      // Older running API builds omit enriched fields. Resolve from the catalog until restarted.
      if (d.items.some(r => !Object.hasOwn(r, 'content'))) {
        const [movies, songs, users] = await Promise.all([request('/Movies'), request('/Music'), allPages('/Users')])
        const contents = new Map([...movies, ...songs].map(c => [c.noiDungID, c]))
        const names = new Map(users.items.map(u => [u.taiKhoanID, u.tenDangNhap]))
        for (const r of d.items) {
          const c = contents.get(r.noiDungID)
          r.username ??= names.get(r.taiKhoanID)
          r.content ??= c ? {title:c.tieuDe,imageUrl:c.hinhAnh,type:c.loaiNoiDung,artist:c.nhac?.tenNgheSi,album:c.nhac?.album,genre:c.phim?.theLoai ?? c.nhac?.genre} : null
        }
      }
      panel('<h1>User feedback</h1>'+table(['Users','Content','Stars','Comment','Time'],d.items.map(r=>'<tr data-updated="'+esc(r.thoiGian)+'">'+cell(r.username ?? r.taiKhoanID)+feedbackContent(r)+cell(r.soSao)+cell(r.nhanXet)+cell(date(r.thoiGian))+'</tr>'))+pager(d,page))
      fillMissingImages(host)
    } else if (tab === 'recommend') {
      const moods=await request('/MoodMapping')
      panel(`<h1>Test mood recommendations</h1><p>Each request saves a mood history entry for the signed-in account.</p><form id="recommend-form"><label class="field">Moods<select name="tamTrangID" required>${moods.map(m=>`<option value="${m.tamTrangID}">${esc(m.tenTamTrang)}</option>`).join('')}</select></label><select name="loaiNoiDung"><option>Movie</option><option>Music</option></select><label class="field">Strategy<select name="mode"><option value="hybrid">Hybrid: mood + content + collaborative</option><option value="content">Content-based + feedback</option><option value="mood">Mood only</option></select></label>${button('Get recommendations')}</form><div id="recommend-results"></div>`)
      bindForm('#recommend-form',async data=>{
        const d=await request('/Recommendation',{method:'POST',body:JSON.stringify({...data,tamTrangID:Number(data.tamTrangID),limit:12})})
        host.querySelector('#recommend-results').innerHTML='<p>'+esc(d.algorithm)+'</p>'+table(['Content','Mood match','Ranking score'],d.items.map(x=>'<tr>'+cell(x.content.tieuDe)+cell(x.match+'%')+cell(x.score==null?'—':x.score.toFixed(3))+'</tr>'))
      })
    } else if (tab === 'history') {
      const d=await allPages('/History')
      panel('<h1>My mood history</h1>'+table(['Moods','Valence','Arousal','Time'],d.items.map(x=>'<tr data-updated="'+esc(x.thoiGian)+'">'+cell(x.tenTamTrang)+cell(x.valence)+cell(x.arousal)+cell(date(x.thoiGian))+'</tr>'))+pager(d,page))
    } else if (tab === 'models') {
      const d=await request('/Models')
      const runtime=d.runtime ?? {}
      const runtimeStatus=runtime.runtimeStatus ?? 'Unavailable'
      const engineer = String(user.vaiTro).toLowerCase() === 'aiengineer'
      const audit = !modelSection ? await request('/Models/audit') : []
      const mapping = engineer && modelSection === 'aiMapping' ? await request('/Models/mapping') : { labels: [], moods: [] }
      const evaluation = modelSection === 'aiEvaluation' || (!engineer && !modelSection) ? await request('/Models/evaluation') : { available: false }
      const coverage = engineer && modelSection === 'aiMapping' ? await request('/Models/coverage') : []
      const report=evaluation.report
      const percent=value=>Number.isFinite(value)?(value*100).toFixed(2)+'%':'—'
      const metricPanel=evaluation.available?'<section class="panel"><h2>Offline evaluation</h2><p>Model: '+esc(report.run_id)+' · Language: '+esc(report.language_evaluated)+' · Benchmark: '+esc(report.test_status)+'</p>'+table(['Metric','Value'],[['Micro-F1',percent(report.full_test?.micro_f1)],['Macro-F1',percent(report.full_test?.macro_f1)],['Selective precision',percent(report.selective_top1?.top1_precision)],['Selective coverage',percent(report.selective_top1?.coverage)]].map(([name,v])=>'<tr>'+cell(name)+cell(v)+'</tr>'))+'<p>Reported offline benchmark, not per-request confidence or Moodify-domain accuracy.</p></section>':'<p>Evaluation report unavailable for this model.</p>'
      panel('<h1>AI model registry</h1>'+
        '<section class="panel"><h2>Inference runtime</h2><p>Runtime: <strong>'+esc(runtimeStatus)+'</strong></p>'+
        '<p>Enabled: '+esc(runtime.enabled)+' · Device: '+esc(runtime.inferenceDevice ?? '—')+' · '+esc(runtime.deviceName ?? '—')+'</p>'+
        '<p>Language: '+esc(runtime.language ?? '—')+' · Mapping: '+esc(runtime.mappingVersion ?? '—')+'</p>'+
        '<p>Artifact SHA-256: <code>'+esc(runtime.artifactHash ?? 'not configured')+'</code></p>'+
        '<p>Config SHA-256: <code>'+esc(runtime.configHash ?? 'not ready')+'</code></p>'+
        '<p>Active registry version: '+esc(d.activeVersion ?? 'none')+' · Registry revision: '+esc(d.revision ?? 0)+' · previous: '+esc(d.previousVersion ?? 'none')+'</p>'+
        '<p>Runtime readiness and persistent active registry state are independent. Weights stay on the server. Training jobs are managed separately; importing metrics never activates a model.</p></section>'+
        '<section class="panel"><h2>Allowlisted model versions</h2>'+table(['Model','Version','Artifact hash','Validation','Runtime','State','Actions'],(d.items ?? []).map(x=>'<tr>'+
          cell(x.displayName)+cell(x.modelVersion)+cell(x.artifactHash)+cell(x.validationStatus)+cell(x.runtimeStatus)+cell(x.isActive?'Active':'Inactive')+
          '<td><button class="secondary-button" data-model-action="validate" data-version="'+esc(x.modelVersion)+'">Validate</button> '+
          '<button class="secondary-button" data-model-action="activate" data-version="'+esc(x.modelVersion)+'" '+(x.validationStatus!=='validated'||x.runtimeStatus!=='Ready'||x.isActive?'disabled':'')+'>Activate</button></td></tr>'))+
        '<button id="model-deactivate" class="secondary-button" '+(d.activeVersion?'':'disabled')+'>Deactivate active</button> '+
        '<button id="model-rollback" class="secondary-button" '+(d.previousVersion?'':'disabled')+'>Rollback to previous</button></section>'+
        '<section class="panel"><h2>Test English text</h2><form id="model-test">'+field('text','English sentence','','text','required minlength="1" maxlength="4000"')+
        '<input type="hidden" name="language" value="en">'+button('Run real inference')+'</form><div id="model-test-result" role="status"></div></section>'+
        metricPanel+
        '<section class="panel"><h2>Local content coverage</h2><p>Pending annotations remain eligible in experimental recommendations. Distance is not a relevance-quality score.</p>'+table(['Mood','Type','Total','Reviewed','Pending','Nearest ID','Squared distance'],coverage.map(x=>'<tr>'+cell(x.mood)+cell(x.type)+cell(x.total)+cell(x.reviewed)+cell(x.pending)+cell(x.nearestContentId ?? '—')+cell(x.nearestDistanceSquared ?? '—')+'</tr>'))+'</section>'+
        '<section class="panel"><h2>Approve emotion-to-mood mapping</h2><p>Version: '+esc(mapping.version)+' · Revision: '+esc(mapping.revision)+'. Experimental mapping; users still confirm every suggestion.</p><form id="mapping-approve">'+table(['Emotion label','Mood','State'],mapping.labels.map(x=>'<tr>'+cell(x.label)+'<td><select name="'+esc(x.label)+'"><option value="">Unmapped</option>'+(x.moodId && !mapping.moods.some(m=>m.id===x.moodId)?'<option selected value="'+esc(x.moodId)+'">Missing mood #'+esc(x.moodId)+'</option>':'')+mapping.moods.map(m=>'<option value="'+esc(m.id)+'" '+(m.id===x.moodId?'selected':'')+'>'+esc(m.name)+'</option>').join('')+'</select></td>'+cell(x.status)+'</tr>'))+button('Approve new mapping version')+'</form></section>'+
        '<section class="panel"><h2>Registry audit (latest 100)</h2>'+table(['Actor','Role','Action','Model','Outcome','Time','Detail'],audit.map(x=>'<tr>'+cell(x.actorId)+cell(x.actorRole)+cell(x.action)+cell(x.modelVersion ?? '—')+cell(x.outcome)+cell(date(x.createdAt))+cell(x.detail)+'</tr>'))+'</section>')
      bindForm('#mapping-approve',async data=>{
        await request('/Models/mapping',{method:'PUT',body:JSON.stringify({expectedRevision:mapping.revision,labels:mapping.labels.map(x=>({label:x.label,moodId:data[x.label]?Number(data[x.label]):null}))})})
        await reload()
      })
      const registryAction=async(path,body)=>{await request(path,{method:'POST',body:body?JSON.stringify(body):'{}'});await reload()}
      host.querySelectorAll('[data-model-action]').forEach(button=>button.addEventListener('click',async()=>{
        button.disabled=true
        try{
          const action=button.dataset.modelAction
          const version=encodeURIComponent(button.dataset.version)
          if(action==='validate') await registryAction('/Models/'+version+'/validate')
          else await registryAction('/Models/'+version+'/activate',{expectedRevision:d.revision})
        }catch(error){showError(error.message);button.disabled=false}
      }))
      host.querySelector('#model-deactivate')?.addEventListener('click',async()=>{
        try{await registryAction('/Models/deactivate',{expectedRevision:d.revision})}catch(error){showError(error.message)}
      })
      host.querySelector('#model-rollback')?.addEventListener('click',async()=>{
        try{await registryAction('/Models/rollback',{expectedRevision:d.revision})}catch(error){showError(error.message)}
      })
      bindForm('#model-test',async data=>{
        const result=await request('/Models/test',{method:'POST',body:JSON.stringify(data)})
        const target=host.querySelector('#model-test-result')
        if (target) target.innerHTML='<p>Status: '+esc(result.status)+' · Model: '+esc(result.modelVersion ?? '—')+'</p>'+
          '<p>Runtime device: '+esc(runtime.inferenceDevice ?? '—')+' '+esc(runtime.deviceName ?? '')+'. Scores are uncalibrated, not accuracy percentages. Confirmation is always required.</p>'+
          '<p>Threshold-qualified decision status: '+esc(result.status)+' · Proposed mood: '+esc(result.suggestedMoodId ?? 'manual choice required')+'</p>'+
          table(['Top-5 display label','Score','Mood','Passes decision policy'],(result.candidates ?? []).map(x=>'<tr>'+cell(x.label)+cell(x.score)+cell(x.moodId ?? 'Unmapped')+cell(x.decisionEligible?'Yes':'No')+'</tr>'))+
          table(['Decision-eligible label','Score','Mood'],(result.decisionCandidates ?? []).map(x=>'<tr>'+cell(x.label)+cell(x.score)+cell(x.moodId ?? 'Unmapped')+'</tr>'))
      })
    }
    if (tab === 'models') {
      const sections = { aiMapping: ['Approve emotion-to-mood mapping', 'Local content coverage'], aiEvaluation: ['Offline evaluation'], aiTest: ['Test English text'], aiAudit: ['Registry audit (latest 100)'] }
      const keep = modelSection ? sections[modelSection] : ['Inference runtime', 'Allowlisted model versions', 'Registry audit (latest 100)', ...(String(user.vaiTro).toLowerCase() === 'aiengineer' ? [] : ['Offline evaluation'])]
      host.querySelectorAll('section.panel').forEach(p => { if (!keep?.includes(p.querySelector('h2')?.textContent)) p.remove() })
      const title = { aiMapping: 'Emotion mapping', aiEvaluation: 'Evaluation', aiTest: 'Inference lab', aiAudit: 'Audit log' }[modelSection]
      if (title && host.querySelector('h1')) host.querySelector('h1').textContent = title
    }
    if(tab !== 'overview' && tab !== 'recommend' && tab !== 'models') enhanceList(host, {nodes:tab === 'moods' ? [...host.querySelectorAll('details')] : undefined,filterColumn:tab === 'users' ? 2 : ['feedback','models'].includes(tab) ? 2 : undefined,filterLabel:tab === 'users' ? 'Role' : tab === 'models' ? 'Status' : 'Stars',dateLabel:['history','feedback'].includes(tab)?'Date':'Updated',dateAvailable:tab!=='models'})
    host.querySelectorAll('[data-page]').forEach(b=>b.addEventListener('click',()=>mountManagement(host,tab,user,Number(b.dataset.page),search)))
  } catch (error) {
    panel('<p>Unable to load data.</p><button id="retry" class="primary-button">Retry</button>')
    showError(error.message)
    host.querySelector('#retry')?.addEventListener('click',reload)
  }
}

function feedbackContent(r) {
  const c = r.content
  if (!c) return cell(r.noiDungID + ' (content unavailable)')
  const image = /^https?:\/\//i.test(c.imageUrl ?? '') ? c.imageUrl : ''
  return '<td><div style="display:flex;align-items:center;gap:12px;white-space:normal;min-width:220px"><img src="'+esc(image)+'" alt="'+esc(c.title)+'" style="width:48px;height:64px;object-fit:cover;border-radius:6px"><div><strong>'+esc(c.title)+'</strong><br><small>'+esc([c.type,c.artist,c.album,c.genre].filter(Boolean).join(' · '))+'</small><br><small>ID: '+esc(r.noiDungID)+'</small></div></div></td>'
}

async function showUserActivity(id) {
  const dialog = document.createElement('dialog')
  dialog.style.cssText = 'width:min(900px,90vw);max-height:85vh;overflow:auto;background:#171a22;color:#eee;border:1px solid #353a48;border-radius:12px;padding:24px'
  dialog.innerHTML = '<button data-close class="secondary-button" style="float:right">Close</button><h2>User activity</h2><label>History <select data-kind><option value="interactions">Content interactions</option><option value="recommendations">Recommendations</option><option value="ratings">Ratings</option></select></label><p>Opening a player or link does not confirm completed listening or viewing.</p><div data-results></div><div class="pagination"><button data-prev>Previous</button><span data-page></span><button data-next>Next</button></div>'
  document.body.append(dialog); dialog.showModal()
  dialog.addEventListener('close',()=>dialog.remove())
  dialog.querySelector('[data-close]').onclick=()=>dialog.close()
  let page=1, serial=0
  const load=async()=>{
    const current=++serial, results=dialog.querySelector('[data-results]')
    results.textContent='Loading…'
    dialog.querySelector('[data-prev]').disabled=true;dialog.querySelector('[data-next]').disabled=true
    try {
      const d=await request('/Users/'+encodeURIComponent(id)+'/activity?kind='+dialog.querySelector('[data-kind]').value+'&page='+page)
      if(current!==serial || !dialog.isConnected)return
      results.innerHTML=table(['Content','Action / rating','Time'],d.items.map(x=>'<tr><td>'+(x.imageUrl?'<img src="'+esc(x.imageUrl)+'" alt="" width="48" height="64" style="object-fit:cover;vertical-align:middle;margin-right:8px">':'')+esc(x.title ?? x.contentId ?? x.algorithm ?? 'Recommendation')+(x.recommendedItems?'<p>'+x.recommendedItems.map(i=>esc(i.title)).join(', ')+'</p>':'')+'</td>'+cell(x.action ?? (x.stars?x.stars+' stars · '+(x.comment??''):x.algorithm))+cell(date(x.createdAt))+'</tr>'))
      dialog.querySelector('[data-page]').textContent='Page '+page+' · '+d.total+' results'
      dialog.querySelector('[data-prev]').disabled=page===1;dialog.querySelector('[data-next]').disabled=page*d.pageSize>=d.total
    } catch(e){if(current===serial)results.textContent=e.message}
  }
  dialog.querySelector('[data-kind]').onchange=()=>{page=1;load()}
  dialog.querySelector('[data-prev]').onclick=()=>{page--;load()}
  dialog.querySelector('[data-next]').onclick=()=>{page++;load()}
  await load()
}
