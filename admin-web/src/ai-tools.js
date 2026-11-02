
import { request } from './api.js'
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]))
const post = (url, data) => request(url, { method: 'POST', body: JSON.stringify(data) })
const table = (headers, rows) => `<div class="table-scroll"><table><thead><tr>${headers.map(x => `<th>${esc(x)}</th>`).join('')}</tr></thead><tbody>${rows.join('') || '<tr><td colspan="8">No records.</td></tr>'}</tbody></table></div>`
const cell = x => `<td>${esc(x)}</td>`
const input = (name, label, extra = '') => `<label class="field">${esc(label)}<input name="${name}" ${extra}></label>`
const download = (name, body) => {
  const url = URL.createObjectURL(new Blob([JSON.stringify(body, null, 2)], { type: 'application/json' }))
  const a = document.createElement('a'); a.href = url; a.download = name; a.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
async function file(input) {
  const item = input.files[0]
  if (!item || item.size > 12_000_000) throw new Error('Choose a JSON file under 12 MB.')
  return JSON.parse(await item.text())
}
function section(host, name, title, body) {
  host.querySelector(`[data-ai-tool="${name}"]`)?.remove()
  const node = document.createElement('section'); node.className = 'panel'; node.dataset.aiTool = name
  node.innerHTML = `<h2>${esc(title)}</h2><p role="alert" class="login-error"></p>${body}`; host.append(node)
  const run = action => async event => {
    event.preventDefault()
    const target = event.currentTarget, button = target.matches('form') ? target.querySelector('[type=submit]') : target
    if (button) button.disabled = true
    try { await action(target) } catch (e) { if (node.isConnected) node.querySelector('[role=alert]').textContent = e.message } finally { if (button) button.disabled = false }
  }
  return { node, run }
}

export async function mountPolicyTools(host) {
  const [data, pipelines] = await Promise.all([request('/AiPolicies'), request('/AiWorkspace/pipelines')])
  if (!host.isConnected) return
  const { node, run } = section(host, 'policies', 'Bilingual VA policies', `<p>These policies control Mobile/Web. They choose stable mood IDs and may tighten classifier confidence gates for either language. Model anchors and catalog VA stay separate. Approval invalidates old analysis receipts.</p>
    <details><summary>Create a reviewed policy candidate</summary><form data-policy-create>
    <label class="field">Pipeline<select name="language"><option value="vi">Vietnamese PhoBERT</option><option value="en">English model</option></select></label>
    <label class="field">Allowed moods<select name="moodIds" multiple size="7" required>${pipelines.moods.map(m => `<option selected value="${m.id}">${esc(m.name)} (#${m.id})</option>`).join('')}</select></label>
    ${input('minimumConfidence', 'Additional classifier confidence (regression ignores this value)', 'type="number" min="0" max="1" step="0.01" value="0" required')}
    ${input('reason', 'Reason and review evidence', 'required minlength="10" maxlength="1000"')}
    <button class="primary-button" type="submit">Save candidate</button></form></details>
    ${table(['Language', 'Active policy', 'Revision', 'Actions'], data.active.map(a => `<tr>${cell(a.language)}${cell(a.version)}${cell(a.revision)}<td><button type="button" data-policy-reset="${a.language}">Restore baseline</button></td></tr>`))}
    ${table(['Candidate', 'Language', 'Mood IDs', 'Reason', 'Action'], data.candidates.map(p => `<tr>${cell(p.version)}${cell(p.language)}${cell(p.moodIds?.join(', '))}${cell(p.reason)}<td><button type="button" data-policy-approve="${p.id}" data-language="${p.language}">Approve</button></td></tr>`))}`)
  const reload = () => mountPolicyTools(host)
  node.querySelector('[data-policy-create]').addEventListener('submit', run(async form => {
    const values = Object.fromEntries(new FormData(form))
    await post('/AiPolicies', { language: values.language, moodIds: [...form.elements.moodIds.selectedOptions].map(x => Number(x.value)), minimumConfidence: Number(values.minimumConfidence), reason: values.reason }); await reload()
  }))
  node.querySelectorAll('[data-policy-approve]').forEach(b => b.addEventListener('click', run(async () => {
    const active = data.active.find(x => x.language === b.dataset.language)
    await post(`/AiPolicies/${encodeURIComponent(b.dataset.policyApprove)}/activate`, { expectedRevision: active.revision }); await reload()
  })))
  node.querySelectorAll('[data-policy-reset]').forEach(b => b.addEventListener('click', run(async () => {
    const active = data.active.find(x => x.language === b.dataset.policyReset)
    await post(`/AiPolicies/${b.dataset.policyReset}/reset`, { expectedRevision: active.revision }); await reload()
  })))
}

export async function mountTypedTools(host, tab) {
  const [data, quality] = await Promise.all([request('/AiExperiments'), request('/ModelQuality')])
  if (!host.isConnected) return
  let body = '<p>Separate schemas for six-emotion English/Vietnamese classifiers, legacy seven-label VSMEC, English VA regression and explicit-rating MF. Source/review metadata and immutable splits are required. These controls do not reinterpret GoEmotions datasets.</p>'
  if (tab === 'aiDatasets') body += `<details><summary>Register reviewed dataset</summary><form data-typed-dataset>
    ${input('name', 'Dataset name', 'required minlength="2" maxlength="100"')}
    <label class="field">Task<select name="taskType"><option value="vsmec7">PhoBERT · VSMEC 7 classes</option><option value="va_regression">RoBERTa · VA regression</option><option value="emotion6_en">RoBERTa · 6 emotions (EN)</option><option value="emotion6_vi">PhoBERT · 6 emotions (VI)</option><option value="recommender">Recommender · explicit ratings</option></select></label>
    <label class="field">Purpose<select name="purpose"><option value="training">Training · train/validation/test</option><option value="evaluation">Independent evaluation · validation/test only</option></select></label>
    <label><input name="independentEvaluation" type="checkbox"> Held-out labels/source/splits were independently reviewed for evaluation; not model-generated gold.</label>
    ${input('source', 'Dataset source and permission to use it', 'required minlength="10" maxlength="1000"')}
    ${input('annotationMethod', 'Label review, disagreement handling and split method', 'required minlength="10" maxlength="2000"')}
    ${input('rows', 'Reviewed rows JSON (id, split, reviewed and task-specific labels)', 'type="file" accept=".json" required')}
    <p>VSMEC: text/emotion. VA: text/valence/arousal. MF: userId/contentId/stars/timestamp. Every row needs reviewed=true after human review; do not auto-label ratings as emotions.</p>
    <button type="submit" class="primary-button">Register immutable dataset</button></form></details>`
  body += table(['Dataset', 'Task', 'Language', 'Samples', 'Splits', 'Hash'], data.datasets.map(d => '<tr>' + cell(d.name) + cell(d.taskType) + cell(d.language) + cell(d.count) + cell(JSON.stringify(d.splits)) + cell(d.hash) + '</tr>'))
  if (tab === 'aiTraining') body += `<details open><summary>Prepare a typed training job</summary><form data-typed-job>
    <label class="field">Dataset<select name="datasetId" required>${data.datasets.map(d => `<option value="${esc(d.id)}">${esc(d.name)} · ${esc(d.taskType)}</option>`).join('')}</select></label>
    ${input('modelVersion', 'New immutable version', 'required pattern="[A-Za-z0-9][A-Za-z0-9_-]{2,79}"')}
    <label class="field">Quality policy for evaluation-only jobs<select name="qualityPolicyId"><option value="">None · training/report only, not promotion eligible</option>${quality.policies.map(p=>`<option value="${esc(p.id)}">${esc(p.kind)} · ${esc(p.id)}</option>`).join('')}</select></label>
    ${input('baseVersion', 'Local base version (NLP only)', 'pattern="[A-Za-z0-9][A-Za-z0-9_-]{2,79}"')}
    ${input('epochs', 'Epochs (NLP up to 10; MF up to 1000)', 'type="number" min="1" max="1000" value="3" required')}
    <button type="submit" class="primary-button">Prepare bundle</button></form>
    <p>Download the bundle and run train_experiment.py explicitly on a training host/Colab. CPU is the default. Existing 28-label local worker remains separate. A prepared job does not launch training.</p></details>`
  body += table(['Version', 'Task', 'State', 'Metrics', 'Bundle'], data.jobs.map(j => `<tr>${cell(j.modelVersion)}${cell(j.taskType)}${cell(j.status)}${cell(JSON.stringify(j.report?.metrics || {}))}<td><button data-typed-bundle="${esc(j.id)}" type="button">Download bundle</button></td></tr>`))
  if (tab === 'aiEvaluation' || tab === 'aiTraining') body += `<form data-typed-report><label class="field">Job<select name="jobId" required>${data.jobs.filter(j => !j.report).map(j => `<option value="${esc(j.id)}">${esc(j.modelVersion)} · ${esc(j.taskType)}</option>`).join('')}</select></label>${input('report', 'Training report JSON with provenance and task-specific metrics', 'type="file" accept=".json" required')}<button type="submit" class="primary-button">Submit report for review</button><p>Submitted metrics are not independently verified deployment results.</p></form>`
  const { node, run } = section(host, 'typed', 'Datasets, training and evaluation by model type', body)
  const reload = () => mountTypedTools(host, tab)
  node.querySelector('[data-typed-dataset]')?.addEventListener('submit', run(async form => {
    const values = Object.fromEntries(new FormData(form)); values.language = { vsmec7: 'vi', emotion6_vi: 'vi', emotion6_en: 'en', va_regression: 'en', recommender: 'none' }[values.taskType]
    values.independentEvaluation = form.elements.independentEvaluation.checked; values.rows = await file(form.elements.rows); await post('/AiExperiments/datasets', values); await reload()
  }))
  node.querySelector('[data-typed-job]')?.addEventListener('submit', run(async form => {
    const values = Object.fromEntries(new FormData(form)); values.epochs = Number(values.epochs); if (!values.baseVersion) delete values.baseVersion; if (!values.qualityPolicyId) delete values.qualityPolicyId
    await post('/AiExperiments/jobs', values); await reload()
  }))
  node.querySelectorAll('[data-typed-bundle]').forEach(b => b.addEventListener('click', run(async () => download('typed-training-bundle.json', await request(`/AiExperiments/jobs/${encodeURIComponent(b.dataset.typedBundle)}/bundle`)))))
  node.querySelector('[data-typed-report]')?.addEventListener('submit', run(async form => {
    await post(`/AiExperiments/jobs/${encodeURIComponent(form.elements.jobId.value)}/report`, await file(form.elements.report)); await reload()
  }))
}

export async function mountFactorizationTools(host) {
  const data = await request('/RecommenderModels')
  if (!host.isConnected) return
  const { node, run } = section(host, 'mf', 'Matrix Factorization registry', `<p>Active MF: <strong>${esc(data.state.version || 'None · neighbor CF baseline')}</strong> · Revision ${data.state.revision}. Unknown users/items fall back. Training is offline; imported artifacts are not automatically activated.</p>
    <details><summary>Import a reviewed MF artifact</summary><form data-mf-upload>
    ${input('model', 'MF JSON artifact', 'type="file" accept=".json" required')}
    ${input('source', 'Dataset source, review and temporal evaluation evidence', 'required minlength="10" maxlength="1000"')}
    <label><input name="reviewed" type="checkbox" required> I reviewed the supplied data, usage permission and evaluation protocol.</label>
    <button type="submit" class="primary-button">Register candidate</button></form></details>
    ${table(['Version', 'Validation pairs', 'Test pairs', 'Validation RMSE', 'Test RMSE', 'Gate', 'Action'], data.models.map(m => `<tr>${cell(m.version)}${cell(m.validationCount)}${cell(m.testCount)}${cell(m.validationRmse)}${cell(m.testRmse)}${cell(m.deployable ? 'Passed technical gate' : 'Insufficient / failed')}<td><button type="button" data-mf-activate="${esc(m.version)}" ${!m.deployable ? 'disabled' : ''}>Activate</button></td></tr>`))}
    <p>Gate: at least 20 warm validation and 20 warm test pairs, RMSE no worse than mean baseline. It does not prove superior mood/content/Hybrid ranking.</p>
    <button type="button" data-mf-deactivate>Use neighbor baseline</button> <button type="button" data-mf-rollback ${!data.state.previousVersion ? 'disabled' : ''}>Rollback MF</button>`)
  const reload = () => mountFactorizationTools(host)
  node.querySelector('[data-mf-upload]').addEventListener('submit', run(async form => {
    await post('/RecommenderModels', { model: await file(form.elements.model), source: form.elements.source.value, reviewed: form.elements.reviewed.checked }); await reload()
  }))
  node.querySelectorAll('[data-mf-activate]').forEach(b => b.addEventListener('click', run(async () => {
    await post(`/RecommenderModels/${encodeURIComponent(b.dataset.mfActivate)}/activate`, { expectedRevision: data.state.revision }); await reload()
  })))
  for (const action of ['deactivate', 'rollback']) node.querySelector(`[data-mf-${action}]`).addEventListener('click', run(async () => {
    await post(`/RecommenderModels/${action}`, { expectedRevision: data.state.revision }); await reload()
  }))
}

export async function mountLifecycleTools(host, kind) {
  let data
  try { data = await request(`/ModelLifecycle/${kind}`) } catch { return }
  if (!host.isConnected) return
  const { node, run } = section(host, 'lifecycle', 'Model lifecycle controls', `<p>Revision ${data.revision} · Active ${esc(data.active_version || 'None')} · Previous ${esc(data.previous_version || 'None')}. Deactivation survives restart. Readiness validation does not measure independent accuracy.</p>
    <form data-lifecycle-validate>${input('version', 'Stored candidate version', 'required pattern="[A-Za-z0-9][A-Za-z0-9_-]{2,79}"')}<button type="submit" class="secondary-button">Validate loading and readiness</button></form><p data-validation-result role="status"></p>
    <button type="button" data-lifecycle-deactivate>Deactivate this pipeline</button>
    <button type="button" data-lifecycle-rollback ${!data.previous_version ? 'disabled' : ''}>Rollback previous artifact</button>`)
  node.querySelector('[data-lifecycle-validate]').addEventListener('submit', run(async form => {
    const result = await post(`/ModelLifecycle/${kind}/validate`, { version: form.elements.version.value, expectedRevision: data.revision })
    if (node.isConnected) node.querySelector('[data-validation-result]').textContent = `${result.status} · quality validation is separate.`
  }))
  for (const action of ['deactivate', 'rollback']) node.querySelector(`[data-lifecycle-${action}]`).addEventListener('click', run(async () => {
    await post(`/ModelLifecycle/${kind}/${action}`, { expectedRevision: data.revision }); await mountLifecycleTools(host, kind)
  }))
}

export async function mountInferenceMetrics(host) {
  const rows = await request('/AiPolicies/metrics')
  if (!host.isConnected) return
  section(host, 'metrics', 'Inference observations by version', '<p>Daily aggregate latency and acceptance counts; no descriptions, identities or user-level telemetry. Acceptance is not classification accuracy.</p>' + table(['Day', 'Language', 'Model', 'Policy', 'Status', 'Count', 'Accepted', 'Mean / max ms'], rows.map(r => '<tr>' + cell(r.day) + cell(r.language) + cell(r.modelVersion) + cell(r.mappingVersion) + cell(r.status) + cell(r.count) + cell(r.accepted) + cell(`${Number(r.meanMs).toFixed(1)} / ${Number(r.maxMs).toFixed(1)}`) + '</tr>')))
}


export async function mountRecommendationExperiments(host) {
  const data = await request('/RecommendationExperiments')
  if (!host.isConnected) return
  const state = data.state
  const { node, run } = section(host, 'bandit', 'Recommendation experiments · contextual bandit', `<p>${esc(data.scope)}</p><p>${esc(data.eventMeaning)}</p><p>Default is off. This is an experimental pilot with a stable account control group, not full multi-step RL or proof of better recommendations.</p>
    <details><summary>Configure pilot after offline review</summary><form data-bandit-config>
    <label><input name="enabled" type="checkbox" ${state.enabled ? 'checked' : ''}> Enable reviewed pilot</label>
    ${input('trafficPercent', 'Pilot traffic (%) — remaining accounts stay in control', `type="number" min="1" max="50" required value="${state.trafficPercent}"`)}
    ${input('epsilon', 'Exploration epsilon', `type="number" min="0.05" max="0.5" step="0.01" required value="${state.epsilon}"`)}
    ${input('reviewEvidence', 'Offline baseline, rollback plan and approved study scope', 'required minlength="20" maxlength="2000"')}
    <button type="submit" class="primary-button">Save versioned experiment</button></form></details>
    ${table(['Experiment', 'Group', 'Arm', 'Generated', 'With feedback', 'Mean reward'], data.groups.map(g => '<tr>' + cell(g.experimentId) + cell(g.group) + cell(g.arm) + cell(g.count) + cell(g.feedbackCount) + cell(g.meanReward) + '</tr>'))}`)
  node.querySelector('[data-bandit-config]').addEventListener('submit', run(async form => {
    await post('/RecommendationExperiments', { expectedRevision: state.revision, enabled: form.elements.enabled.checked,
      trafficPercent: Number(form.elements.trafficPercent.value), epsilon: Number(form.elements.epsilon.value), reviewEvidence: form.elements.reviewEvidence.value })
    await mountRecommendationExperiments(host)
  }))
}

export async function mountQualityTools(host, kind) {
  const owner = host.firstElementChild
  const [data, experiments] = await Promise.all([request('/ModelQuality'), request('/AiExperiments')])
  if (!host.isConnected || host.firstElementChild !== owner) return
  const tasks = kind === 'vi' ? ['vsmec7', 'emotion6_vi'] : ['va_regression', 'emotion6_en']
  const candidates = experiments.jobs.filter(j => tasks.includes(j.taskType) && j.report && j.qualityPolicyId)
  const { node, run } = section(host, 'quality', 'Model promotion · reviewed quality evidence', `<p>${esc(data.evidenceScope)}</p>
    <p>Uploading stores a candidate. Activation and rollback verify its fingerprint against an approval. An existing loaded baseline stays available without being labelled independently validated.</p>
    <details><summary>Fix quality criteria before creating an evaluation job</summary><form data-quality-policy>
    <label class="field">Model task<select name="taskType">${tasks.map(t=>`<option value="${t}">${esc(t)}</option>`).join('')}</select></label>
    ${input('minimumValidationSamples', 'Minimum validation samples', 'type="number" min="20" max="60000" value="50" required')}
    ${input('minimumTestSamples', 'Minimum test samples', 'type="number" min="20" max="60000" value="50" required')}
    <div data-classifier-criteria>
    ${input('minimumMacroF1', 'Minimum macro-F1 across every class', 'type="number" min="0" max="1" step="0.01" value="0.55" required')}
    ${input('minimumCoverage', 'Minimum accepted coverage', 'type="number" min="0" max="1" step="0.01" value="0.20" required')}
    ${input('minimumSelectiveAccuracy', 'Minimum accuracy on accepted samples', 'type="number" min="0" max="1" step="0.01" value="0.80" required')}
    </div><div data-regression-criteria>
    ${input('maximumMae', 'Maximum MAE on each VA axis', 'type="number" min="0.0001" max="2" step="0.01" value="0.40" required')}
    ${input('maximumRmse', 'Maximum RMSE on each VA axis', 'type="number" min="0.0001" max="2" step="0.01" value="0.60" required')}
    </div>
    ${input('reason', 'Protocol, baseline comparison and rationale for these criteria', 'required minlength="30" maxlength="2000"')}
    <p>Prefilled values are starting examples, not scientifically validated acceptance targets. Fix and document criteria before viewing test outcomes.</p>
    <button class="primary-button" type="submit">Save immutable quality policy</button></form></details>
    ${table(['Policy', 'Created', 'Minimum validation / test', 'Reason'], data.policies.filter(p => p.kind === kind).map(p => '<tr>'+cell(p.id)+cell(p.createdAt)+cell(p.minimumValidationSamples+' / '+p.minimumTestSamples)+cell(p.reason)+'</tr>'))}
    <details><summary>Review and approve an evaluated candidate</summary><form data-quality-approve>
    <label class="field">Submitted evaluation job<select name="jobId" required><option value="">Choose a reviewed report</option>${candidates.map(j=>`<option value="${esc(j.id)}">${esc(j.modelVersion)} · ${esc(j.id)}</option>`).join('')}</select></label>
    <label><input name="independentReviewConfirmed" type="checkbox" required> I reviewed independent labels/splits, predictions, baseline comparison and provenance. These are not software-test fixtures.</label>
    ${input('reviewEvidence', 'Reviewers and evidence location; disagreements and deployment decision', 'required minlength="30" maxlength="3000"')}
    <button class="primary-button" type="submit">Check gate and record approval</button></form></details>
    ${table(['Version', 'Evidence scope', 'Policy', 'Bundle SHA-256', 'Reviewed at'], data.approvals.filter(a => a.kind === kind).map(a => '<tr>'+cell(a.version)+cell(a.mode)+cell(a.policyId||'Existing baseline')+cell(a.bundleSha256)+cell(a.createdAt)+'</tr>'))}`)
  const reload = () => node.isConnected ? mountQualityTools(host, kind) : Promise.resolve()
  const policyForm = node.querySelector('[data-quality-policy]')
  const showTaskCriteria = () => {
    const regression = policyForm.elements.taskType.value === 'va_regression'
    for (const [selector, visible] of [['[data-classifier-criteria]', !regression], ['[data-regression-criteria]', regression]]) {
      const group = policyForm.querySelector(selector); group.hidden = !visible
      group.querySelectorAll('input').forEach(control => { control.disabled = !visible })
    }
  }
  policyForm.elements.taskType.addEventListener('change', showTaskCriteria); showTaskCriteria()
  node.querySelector('[data-quality-policy]').addEventListener('submit', run(async form => {
    const values = Object.fromEntries(new FormData(form))
    for (const key of Object.keys(values)) if (key !== 'reason' && key !== 'taskType') values[key] = Number(values[key])
    await post('/ModelQuality/policies', { ...values, kind }); await reload()
  }))
  node.querySelector('[data-quality-approve]').addEventListener('submit', run(async form => {
    await post('/ModelQuality/approvals', { jobId: form.elements.jobId.value,
      independentReviewConfirmed: form.elements.independentReviewConfirmed.checked, reviewEvidence: form.elements.reviewEvidence.value })
    await reload()
  }))
}
