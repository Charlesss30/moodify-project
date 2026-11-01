import { mountPolicyTools, mountTypedTools, mountFactorizationTools, mountLifecycleTools, mountInferenceMetrics, mountRecommendationExperiments, mountQualityTools } from './ai-tools.js'
import { request } from './api.js'
import { enhanceList } from './list-controls.js'

export const aiTabs = { aiOverview: 'AI overview', vaModels: 'English valence/arousal', viModels: 'Vietnamese PhoBERT', models: 'Emotion classifier', aiMapping: 'GoEmotions mapping', aiEvaluation: 'Evaluation', aiDatasets: 'Datasets', aiTraining: 'Training jobs', aiFeedback: 'Feedback analytics', aiTest: 'Inference lab', aiAudit: 'Audit log' }
const esc = (v = '') => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]))
const date = v => new Date(v).toLocaleString('en-US')
const table = (heads, rows) => `<div class="table-scroll"><table><thead><tr>${heads.map(v => `<th>${esc(v)}</th>`).join('')}</tr></thead><tbody>${rows.join('') || '<tr><td colspan="8">No records yet.</td></tr>'}</tbody></table></div>`
const cell = v => `<td>${esc(v)}</td>`
const panel = (title, body) => `<section class="panel"><h2>${esc(title)}</h2>${body}</section>`
const button = (text, attrs = '') => `<button class="secondary-button" type="button" ${attrs}>${esc(text)}</button>`
function download(name, data, text = false) {
  const url = URL.createObjectURL(new Blob([text ? data : JSON.stringify(data, null, 2)], { type: text ? 'text/plain' : 'application/json' }))
  const a = document.createElement('a'); a.href = url; a.download = name; a.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
const post = (path, data) => request('/AiWorkspace/' + path, { method: 'POST', body: JSON.stringify(data) })
async function jsonFile(input, limit = 32_000_000) {
  const file = input.files[0]
  if (!file || file.size > limit) throw new Error('Choose a JSON file within the supported size limit.')
  return JSON.parse(await file.text())
}

export async function mountAiWorkspace(host, tab, user, mountModels) {
  host.innerHTML = '<p role="status">Loading AI workspace…</p>'
  const reload = () => mountAiWorkspace(host, tab, user, mountModels)
  const error = e => { if (host.isConnected) host.querySelector('[role="alert"]').textContent = e.message }
  const render = html => { if (host.isConnected) host.innerHTML = `<div class="ai-workspace"><div class="page-intro"><div><p class="eyebrow">MOODIFY / AI WORKSPACE</p><h1>${esc(aiTabs[tab])}</h1></div>${button('Refresh', 'data-refresh')}</div><p role="alert" class="login-error"></p>${html}</div>` }
  const on = (selector, action) => host.querySelectorAll(selector).forEach(el => el.addEventListener('click', async () => {
    el.disabled = true
    try { await action(el) } catch (e) { error(e) } finally { el.disabled = false }
  }))
  const form = (selector, action) => host.querySelector(selector)?.addEventListener('submit', async e => {
    e.preventDefault(); const target = e.currentTarget; const b = target.querySelector('[type="submit"]'); b.disabled = true
    try { await action(Object.fromEntries(new FormData(target)), target) } catch (err) { error(err) } finally { b.disabled = false }
  })
  try {
    // Existing registry actions retain their server permissions, CAS revisions and audit.
    const sections = { aiMapping: ['Approve emotion-to-mood mapping', 'Local content coverage'], aiEvaluation: ['Offline evaluation'], aiAudit: ['Registry audit (latest 100)'] }
    if (tab === 'aiTest') {
      const pipelines = await request('/AiWorkspace/pipelines')
      render(panel('Bilingual app pipeline', `<p>Uses the same authenticated API and backend routing as Mobile/Web. No recommendation or history is created by this preview.</p><p>AI enabled: <strong>${esc(pipelines.enabled)}</strong> · Routing: ${esc(pipelines.routingVersion)}</p><form id="bilingual-test"><label class="field">Vietnamese or English description<textarea name="text" required maxlength="4000" rows="4" placeholder="Tôi thấy bình yên / I feel peaceful today"></textarea></label><button class="primary-button" type="submit">Analyze automatically</button></form><pre id="bilingual-output" aria-live="polite"></pre>`) + panel('Policy scope', `<p>${esc(pipelines.legacyMappingScope)}</p>${table(['Language', 'Model output', 'VA policy', 'Limitation'], pipelines.policies.map(p => '<tr>' + cell(p.language) + cell(p.modelType) + cell(p.mappingVersion) + cell(p.limitation) + '</tr>'))}`))
      form('#bilingual-test', async values => {
        const started = performance.now()
        const output = host.querySelector('#bilingual-output')
        const result = await request('/MoodAnalysis/valence-arousal', { method: 'POST', body: JSON.stringify({ text: values.text, language: 'auto' }) })
        if (!output?.isConnected) return
        output.textContent = JSON.stringify({ ...result, latencyMs: Math.round(performance.now() - started) }, null, 2)
      })
      on('[data-refresh]', reload)
      return
    }
    if (sections[tab]) {
      await mountModels(host, 'models', user, 1, '', tab)
      const heading = host.querySelector('h1')
      if (heading) heading.textContent = aiTabs[tab]
      host.querySelectorAll('section.panel').forEach(p => { p.hidden = !sections[tab].includes(p.querySelector('h2')?.textContent) })
      if (tab === 'aiMapping') await mountPolicyTools(host)
      if (tab === 'aiEvaluation') {
        await mountTypedTools(host, tab)
        const jobs = await request('/AiWorkspace/jobs')
        const result = document.createElement('section'); result.className = 'panel'
        result.innerHTML = '<h2>Submitted training results</h2><p>Compare runs only when dataset hash and test split match. Submitted metrics are not verified deployment results.</p>' + table(['Version', 'Dataset hash', 'Micro-F1', 'Macro-F1', 'Test samples', 'State'], jobs.filter(j => j.report).map(j => '<tr>' + cell(j.modelVersion) + cell(j.datasetHash) + cell(j.report.microF1) + cell(j.report.macroF1) + cell(j.report.testSamples) + cell(j.status) + '</tr>'))
        host.append(result); enhanceList(result, { dateAvailable: false })
      }
      return
    }
    if (tab === 'vaModels') {
      const data = await request('/Models/va')
      const runtime = data.runtime || {}
      const catalog = data.catalog || {}
      const runtimePanel = panel('Valence/arousal runtime', `<p>Status: <strong>${esc(runtime.status || 'unavailable')}</strong> · Active version: <strong>${esc(runtime.modelVersion || 'None')}</strong> · Device: ${esc(runtime.inferenceDevice || '—')}</p>`)
      render(runtimePanel + panel('Upload a valence/arousal model', `<p>Create a ZIP or RAR containing one <code>.pt</code> checkpoint and the full <code>tokenizer/</code> folder, including <code>tokenizer.json</code>. The checkpoint may be at the archive root or inside one folder. Maximum package size: 2 GB.</p><form id="va-upload-form"><label class="field">New model version<input name="version" required minlength="3" maxlength="80" pattern="[A-Za-z0-9][A-Za-z0-9_-]{2,79}" placeholder="roberta_va_regression_v2"></label><label class="field">Model package (.zip or .rar)<input name="package" type="file" accept=".zip,.rar,application/zip,application/vnd.rar,application/x-rar-compressed" required></label><button class="primary-button" type="submit">Upload candidate</button></form><p>The dashboard stores the candidate without activation. Readiness and reviewed quality approval are separate steps. Existing versions are never overwritten.</p>`) + panel('Available model versions', `<p>Stored means the files are on disk. Only Active means the model is loaded for inference. You can activate a stored version from this list.</p>${table(['Version', 'Checkpoint', 'Tokenizer', 'State', 'Action'], (catalog.items || []).map(item => {
        const version = item.modelVersion ?? item.model_version
        const tokenizerAvailable = item.tokenizerAvailable ?? item.tokenizer_available
        const active = version === runtime.modelVersion || version === runtime.model_version
        return `<tr>${cell(version)}${cell(item.checkpoint)}${cell(tokenizerAvailable ? 'Available' : 'Missing')}${cell(active ? 'Active' : 'Stored')}<td>${active ? '<strong>Active</strong>' : button('Activate', `data-va-activate="${esc(version)}"`)}</td></tr>`
      }))}`))
      const uploadForm = host.querySelector('#va-upload-form')
      uploadForm?.addEventListener('submit', async event => {
        event.preventDefault()
        if (!uploadForm.reportValidity()) return
        const submit = uploadForm.querySelector('[type="submit"]')
        const packageFile = uploadForm.elements.package.files[0]
        if (packageFile.size > 2 * 1024 * 1024 * 1024) {
          error(new Error('The ZIP or RAR package must be smaller than 2 GB.'))
          return
        }
        if (!window.confirm(`Upload candidate ${uploadForm.elements.version.value}? This stores a candidate; activation requires reviewed quality evidence.`)) return
        submit.disabled = true
        submit.textContent = 'Uploading and loading model…'
        try {
          await request('/Models/va/upload', { method: 'POST', body: new FormData(uploadForm) })
          await reload()
        } catch (e) {
          error(e)
          submit.disabled = false
          submit.textContent = 'Upload candidate'
        }
      })
      on('[data-va-activate]', async b => {
        const version = b.dataset.vaActivate
        if (!window.confirm(`Load and activate ${version}? New user analyses will use it.`)) return
        const lifecycle = await request('/ModelLifecycle/va')
        await request('/ModelLifecycle/va/activate', { method: 'POST', body: JSON.stringify({ version, expectedRevision: lifecycle.revision }) })
        await reload()
      })
    } else if (tab === 'viModels') {
      const data = await request('/Models/vi')
      const runtime = data.runtime || {}
      const catalog = data.catalog || {}
      render(panel('Upload a Vietnamese PhoBERT model', `<p>This is a separate Vietnamese classifier. It requires one PhoBERT sequence-classification bundle with <code>config.json</code>, <code>model.safetensors</code> (or <code>pytorch_model.bin</code>), <code>tokenizer_config.json</code>, <code>vocab.txt</code>, and <code>bpe.codes</code>. ZIP or RAR, maximum 2 GB. If training used custom emoji/teencode dictionaries, include <code>preprocessing.json</code> beside the model files so serving uses the same normalization.</p><form id="vi-upload-form"><label class="field">New Vietnamese model version<input name="version" required minlength="3" maxlength="80" pattern="[A-Za-z0-9][A-Za-z0-9_-]{2,79}" placeholder="phobert_vsmec_v2"></label><label class="field">PhoBERT package (.zip or .rar)<input name="package" type="file" accept=".zip,.rar,application/zip,application/vnd.rar,application/x-rar-compressed" required></label><button class="primary-button" type="submit">Upload Vietnamese candidate</button></form><p>Vietnamese candidates are stored and activated independently. Upload does not activate. Quality approval is required before switching the Vietnamese model; the English pipeline stays separate.</p>`) + panel('Vietnamese model runtime', `<p>Status: <strong>${esc(runtime.status || 'unavailable')}</strong> · Active version: <strong>${esc(runtime.modelVersion || 'None')}</strong> · Device: ${esc(runtime.inferenceDevice || '—')} · Language: vi</p><p>Preprocessing: <strong>${esc(runtime.preprocessingStatus || 'Not reported')}</strong> · SHA-256: ${esc(runtime.preprocessingSha256 || '—')}. Bundled files still require training-parity review.</p>`) + panel('Available Vietnamese versions', `<p>Stored means the files are on disk, even if an earlier activation failed. Only Active means PhoBERT is loaded and ready.</p>${table(['Version', 'Checkpoint', 'Tokenizer', 'State', 'Action'], (catalog.items || []).map(item => {
        const version = item.modelVersion ?? item.model_version
        const active = version === runtime.modelVersion || version === catalog.activeVersion
        return `<tr>${cell(version)}${cell(item.checkpoint)}${cell((item.tokenizerAvailable ?? item.tokenizer_available) ? 'Available' : 'Missing')}${cell(active ? 'Active' : 'Stored')}<td>${active ? '<strong>Active</strong>' : button('Activate', `data-vi-activate="${esc(version)}"`)}</td></tr>`
      }))}`))
      const uploadForm = host.querySelector('#vi-upload-form')
      uploadForm?.addEventListener('submit', async event => {
        event.preventDefault()
        if (!uploadForm.reportValidity()) return
        const submit = uploadForm.querySelector('[type="submit"]')
        const packageFile = uploadForm.elements.package.files[0]
        if (packageFile.size > 2 * 1024 * 1024 * 1024) { error(new Error('Vietnamese model package must be smaller than 2 GB.')); return }
        const version = uploadForm.elements.version.value
        if (!window.confirm(`Upload Vietnamese candidate model ${version}? The English model will stay unchanged.`)) return
        submit.disabled = true
        submit.textContent = 'Uploading and loading PhoBERT…'
        try {
          await request('/Models/vi/upload', { method: 'POST', body: new FormData(uploadForm) })
          await reload()
        } catch (e) {
          error(e)
          submit.disabled = false
          submit.textContent = 'Upload Vietnamese candidate'
        }
      })
      on('[data-vi-activate]', async b => {
        const version = b.dataset.viActivate
        if (!window.confirm(`Activate Vietnamese model ${version}? The English model will stay unchanged.`)) return
        const lifecycle = await request('/ModelLifecycle/vi')
        await request('/ModelLifecycle/vi/activate', { method: 'POST', body: JSON.stringify({ version, expectedRevision: lifecycle.revision }) })
        await reload()
      })
    } else if (tab === 'aiOverview') {
      const [registry, datasets, jobs, feedback, pipelines] = await Promise.all([request('/Models'), request('/AiWorkspace/datasets'), request('/AiWorkspace/jobs'), request('/AiWorkspace/feedback'), request('/AiWorkspace/pipelines')])
      const r = registry.runtime || {}
      const va = pipelines.english || {}
      const vi = pipelines.vietnamese || {}
      const stats = [['App AI enabled', pipelines.enabled], ['English VA runtime', va.status || 'Unavailable'], ['English VA model', va.modelVersion || 'None'], ['Vietnamese runtime', vi.status || 'Unavailable'], ['Vietnamese model', vi.modelVersion || 'None'], ['Legacy classifier runtime', r.runtimeStatus || 'Unavailable'], ['Legacy classifier', registry.activeVersion || 'None'], ['Datasets (latest 200)', datasets.items.length], ['Jobs (latest 200)', jobs.length], ['Submitted results', jobs.filter(j => j.report).length], ['Ratings (latest 20,000)', feedback.count]]
      render(`<p>Hello, ${esc(user.tenDangNhap || 'AI engineer')}. Manage data, experiments and model delivery.</p><div class="stats-grid">${stats.map(([label, value]) => `<div class="stat-card"><div class="stat-copy"><span>${esc(label)}</span><strong>${esc(value)}</strong></div></div>`).join('')}</div>` + panel('Mobile/Web bilingual analysis', `<p>Auto routing: ${esc(pipelines.routingVersion)}. Vietnamese uses PhoBERT; English uses VA regression. No fallback to another language model when the selected runtime fails.</p>${table(['Language', 'Output', 'Policy', 'Limitation'], pipelines.policies.map(p => '<tr>' + cell(p.language) + cell(p.modelType) + cell(p.mappingVersion) + cell(p.limitation) + '</tr>'))}<p>Confirmations use server-held analysis receipts (5 minutes), then the shared Hybrid recommender. Inference lab tests this same app endpoint.</p>`) + panel('Mood geometry used by both app models', table(['ID', 'Mood', 'Valence range', 'Arousal range'], pipelines.moods.map(m => '<tr>' + cell(m.id) + cell(m.name) + cell(`${m.minValence} to ${m.maxValence}`) + cell(`${m.minArousal} to ${m.maxArousal}`) + '</tr>'))) + panel('Independent legacy classifier and training', `<p>${esc(pipelines.legacyMappingScope)}</p><p>${esc(pipelines.trainingScope)}</p><p>Review datasets and evaluation in their matching pipeline. A runtime marked Ready is not a quality evaluation. Existing local/Colab training remains separate from serving.</p>`))
    } else if (tab === 'aiDatasets') {
      const d = await request('/AiWorkspace/datasets')
      render(panel('Register an immutable English emotion dataset', `<p>JSON array: text, labels, split (train / validation / test). Maximum 60,000 reviewed samples / 32 MB upload (15 MB stored). Training requires at least 400 validation samples for selection/calibration. Duplicate text across splits is rejected. Do not include personal identifiers.</p><details><summary>Accepted labels</summary><p>${esc(d.labels.join(', '))}</p></details><form id="dataset-create"><label class="field">Dataset name<input name="name" required minlength="2" maxlength="100"></label><label class="field">Source and labeling method<input name="source" required minlength="3" maxlength="300"></label><label class="field">Samples JSON<input name="file" type="file" accept=".json,application/json" required></label><div class="ai-actions"><button class="primary-button" type="submit">Register dataset</button>${button('Download format example', 'data-example')}</div></form><p>The three-row example documents the format only; it is not a quality training dataset.</p>`) + panel('Dataset versions (latest 200)', table(['Name', 'Source', 'Samples', 'Split counts', 'Created', 'Actions'], d.items.map(x => `<tr data-updated="${esc(x.createdAt)}">${cell(x.name)}${cell(x.source)}${cell(x.count)}${cell(JSON.stringify(x.splits))}${cell(date(x.createdAt))}<td>${button('Export JSON', `data-dataset="${esc(x.id)}"`)}</td></tr>`))))
      form('#dataset-create', async (values, f) => { await post('datasets', { name: values.name, source: values.source, rows: await jsonFile(f.elements.file) }); await reload() })
      on('[data-dataset]', async b => download(`dataset-${b.dataset.dataset}.json`, await request('/AiWorkspace/datasets/' + b.dataset.dataset)))
      on('[data-example]', () => download('emotion-samples-format.json', [{ text: 'I am glad to meet you.', labels: ['joy'], split: 'train' }, { text: 'I feel afraid of the dark.', labels: ['fear'], split: 'validation' }, { text: 'Thank you for helping me.', labels: ['gratitude'], split: 'test' }]))
      enhanceList(host.querySelectorAll('section.panel')[1], { dateAvailable: true })
    } else if (tab === 'aiTraining') {
      const [datasets, jobs] = await Promise.all([request('/AiWorkspace/datasets'), request('/AiWorkspace/jobs')])
      render(panel('Prepare external training', `<p>Creates a persisted experiment and downloadable configuration. No GPU process starts on the API server.</p><form id="job-create"><div class="form-row"><label class="field">Dataset<select name="datasetId" required><option value="">Select dataset</option>${datasets.items.map(d => `<option value="${esc(d.id)}">${esc(d.name)} (${d.count})</option>`).join('')}</select></label><label class="field">New model version<input name="modelVersion" required pattern="[a-zA-Z0-9][a-zA-Z0-9_-]{2,79}" placeholder="roberta_experiment_02"></label></div><div class="form-row"><label class="field">Epochs<input name="epochs" type="number" min="1" max="10" value="6" required></label><label class="field">Batch size<input name="batchSize" type="number" min="1" max="8" value="2" required></label><label class="field">Learning rate<input name="learningRate" type="number" min="0.000001" max="0.0001" step="0.000001" value="0.00001" required></label><label class="field">Gradient accumulation<input name="gradientAccumulation" type="number" min="1" max="64" value="16" required></label><label class="field">Seed<input name="seed" type="number" min="0" max="2147483647" value="42" required></label></div><button class="primary-button" type="submit">Prepare job</button></form>`) + panel('Run in Google Colab', '<p>Download the job notebook and open it in Colab with a GPU runtime. It uses your original RoBERTa-large ASL notebook, including selection/calibration splits, tuned thresholds and independent test evaluation.</p><p>Run cells in order. The final cell downloads workspace_report.json to import below. Google Drive stores each job in its own workspace-jobs folder. No training runs on this API server.</p><p>Cancel below closes the dashboard record only. Stop the actual run in Colab. Existing deployment weights are never overwritten.</p>') + panel('Training jobs (latest 200)', table(['Version', 'Status', 'Dataset', 'Created', 'Actions'], jobs.map(j => `<tr data-updated="${esc(j.createdAt)}">${cell(j.modelVersion)}${cell(j.status)}${cell(datasets.items.find(d => d.id === j.datasetId)?.name || j.datasetId)}${cell(date(j.createdAt))}<td><div class="ai-actions">${button('Colab notebook', `data-notebook="${esc(j.id)}"`)}${button('Bundle', `data-bundle="${esc(j.id)}"`)}${button('Details', `data-details="${esc(j.id)}"`)}${['prepared', 'running'].includes(j.status) ? `${j.status === 'prepared' ? button('Mark running', `data-state="running" data-job="${esc(j.id)}"`) : ''}${button('Import result', `data-report="${esc(j.id)}"`)}${button('Mark failed', `data-state="failed" data-job="${esc(j.id)}"`)}${button('Cancel record', `data-state="cancelled" data-job="${esc(j.id)}"`)}` : ''}</div></td></tr>`))) + '<div id="job-detail"></div><input id="result-file" type="file" accept=".json,application/json" hidden>')
      form('#job-create', async values => { for (const k of ['epochs', 'batchSize', 'gradientAccumulation', 'learningRate', 'seed']) values[k] = Number(values[k]); await post('jobs', values); await reload() })
      on('[data-notebook]', async b => download(`training-${b.dataset.notebook}.ipynb`, await request(`/AiWorkspace/jobs/${b.dataset.notebook}/notebook`)))
      on('[data-bundle]', async b => download(`job-${b.dataset.bundle}.json`, await request(`/AiWorkspace/jobs/${b.dataset.bundle}/bundle`)))
      on('[data-details]', b => { const j = jobs.find(x => x.id === b.dataset.details); host.querySelector('#job-detail').innerHTML = panel('Job provenance and events', `<pre>${esc(JSON.stringify(j, null, 2))}</pre>`) })
      on('[data-state]', async b => { const j = jobs.find(x => x.id === b.dataset.job); await post(`jobs/${j.id}/state`, { state: b.dataset.state, revision: j.revision }); await reload() })
      let selectedJob
      on('[data-report]', b => { selectedJob = jobs.find(x => x.id === b.dataset.report); host.querySelector('#result-file').click() })
      host.querySelector('#result-file').addEventListener('change', async e => {
        const j = selectedJob
        try { const report = await jsonFile(e.target, 64_000); await post(`jobs/${j.id}/report`, { ...report, revision: j.revision }); await reload() } catch (err) { error(err); e.target.value = '' }
      })
      enhanceList(host.querySelectorAll('section.panel')[2], { filterColumn: 1, filterLabel: 'Status' })
    } else if (tab === 'aiFeedback') {
      const d = await request('/AiWorkspace/feedback')
      render(panel('Feedback for recommendation research', `<p>${esc(d.note)} These ratings are not GoEmotions labels and cannot directly train emotion recognition.</p><p>${d.distribution.map(x => `${x.stars} stars: ${x.count}`).join(' · ')}</p>${button('Export pseudonymous ratings', 'data-export-feedback')}`) + panel('Content ratings', table(['Content', 'ID', 'Ratings', 'Average stars'], d.items.map(x => '<tr>' + cell(x.title) + cell(x.contentId) + cell(x.count) + cell(x.average) + '</tr>'))))
      on('[data-export-feedback]', async () => download('recommendation-ratings.json', await post('feedback/export', {})))
      enhanceList(host.querySelectorAll('section.panel')[1], { dateAvailable: false })
    }
    if (tab === 'aiDatasets' || tab === 'aiTraining') await mountTypedTools(host, tab)
    if (tab === 'aiTraining' || tab === 'aiFeedback') await mountFactorizationTools(host)
    if (tab === 'vaModels' || tab === 'viModels') await mountLifecycleTools(host, tab === 'vaModels' ? 'va' : 'vi')
    if (tab === 'vaModels' || tab === 'viModels') await mountQualityTools(host, tab === 'vaModels' ? 'va' : 'vi')
    if (tab === 'aiOverview') await mountInferenceMetrics(host)
    if (tab === 'aiFeedback') await mountRecommendationExperiments(host)
    on('[data-refresh]', reload)
  } catch (e) {
    render(button('Retry', 'data-retry')); error(e); on('[data-retry]', reload)
  }
}
