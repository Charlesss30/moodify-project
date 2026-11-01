import { chromium } from 'playwright-core'
import assert from 'node:assert/strict'
import fs from 'node:fs'
const origin = process.env.SMOKE_ORIGIN
const folder = '../.local-build/phase2-flutter-browser'
fs.mkdirSync(folder, { recursive: true })
const browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--disable-gpu'] })
try {
  const page = await browser.newPage({ viewport: { width: 430, height: 1000 } })
  page.setDefaultTimeout(25000)
  const enter = async (locator, value) => {
    const bounds = await locator.boundingBox()
    // Flutter semantics may include the helper/button beneath an input.
    // Tap the upper text area instead of the center of that larger box.
    await locator.click({ force: true, position: { x: Math.min(32, bounds.width/2), y: Math.min(28, bounds.height/2) } })
    await page.waitForTimeout(250)
    await locator.press('ControlOrMeta+A')
    await page.keyboard.insertText(value)
    await page.waitForTimeout(100)
    assert.equal(await locator.inputValue(), value)
    await page.keyboard.press('Tab')
    await page.waitForTimeout(250)
  }
  const errors = []
  const histories = []
  page.on('response', async response => {
    if (/\/api\/History(?:\?|$)/.test(response.url()) && response.request().method() === 'GET' && response.status() === 200) {
      histories.push(await response.json())
    }
  })
  page.on('pageerror', error => errors.push(error.message))
  await page.route('https://open.spotify.com/embed/**', route => route.fulfill({ contentType: 'text/html', body: '<p>External player transport excluded from smoke.</p>' }))
  await page.route('https://www.youtube.com/embed/**', route => route.fulfill({ contentType: 'text/html', body: '<p>External video transport excluded from smoke.</p>' }))
  try {
    const login = async () => {
    await page.goto(origin, { waitUntil: 'domcontentloaded' })
    const placeholder = page.locator('flt-semantics-placeholder')
    await placeholder.waitFor({ state: 'attached', timeout: 45000 })
    await placeholder.evaluate(element => element.click())
    await enter(page.getByRole('textbox', { name: 'Email hoặc tên đăng nhập', exact: true }), process.env.SMOKE_USER)
    await enter(page.getByRole('textbox', { name: 'Mật khẩu', exact: true }), process.env.SMOKE_PASSWORD)
        const [login] = await Promise.all([
      page.waitForResponse(r => r.url().endsWith('/api/Auth/login') && r.request().method() === 'POST'),
      page.getByRole('button', { name: 'Đăng nhập', exact: true }).click(),
    ])
    assert.equal(login.status(), 200)
        const skip = page.getByRole('button', { name: 'Để sau', exact: true })
    await skip.or(page.getByRole('tab', { name: 'Trang chủ', exact: true })).first().waitFor()
    if (await skip.isVisible()) await skip.click()
    await page.waitForTimeout(450)
    }
    const nav = async label => {
      const tabs = page.getByRole('tab', { name: new RegExp(label) })
      if (await tabs.count()) return tabs.last().click({ force: true })
      return page.getByRole('button', { name: new RegExp(label) }).last().click({ force: true })
    }
    const clickVisible = async locator => {
      for (let i = 0; i < 12; i++) {
        const box = await locator.first().boundingBox().catch(() => null)
        if (box && box.y >= 0 && box.y + box.height <= 990) return locator.first().click()
        await page.mouse.wheel(0, 450)
        await page.waitForTimeout(200)
      }
      await locator.first().click()
    }
    const records = []
    for (const [language, text] of [
      ['vi', 'Tôi rất vui vì hôm nay được gặp lại bạn thân.'],
      ['en', 'I am thrilled that I got my dream job! I want to celebrate and dance all night.'],
    ]) {
      // Each language runs a fresh app session; the backend account/history stays the same.
      await login()
      await nav('Khám phá')
      await page.waitForTimeout(450)
      await page.mouse.wheel(0, -12000)
            console.log('Flutter E2E language=' + language + ': entering description')
      const description = page.getByRole('textbox', { name: /Mô tả tâm trạng/ })
      await enter(description, text)
      const [analysisResponse] = await Promise.all([
        page.waitForResponse(r => r.url().endsWith('/api/MoodAnalysis/valence-arousal') && r.request().method() === 'POST'),
        clickVisible(page.getByRole('button', { name: 'Phân tích tâm trạng', exact: true })),
      ])
      const analysis = await analysisResponse.json()
      assert.equal(analysisResponse.request().postDataJSON().text, text)
      assert.equal(analysisResponse.status(), 200)
      assert.equal(analysis.language, language)
      assert.equal(analysis.accepted, true)
      const [recommendationResponse] = await Promise.all([
        page.waitForResponse(r => r.url().endsWith('/api/Recommendation') && r.request().method() === 'POST'),
        clickVisible(page.getByRole('button', { name: 'Xác nhận và nhận gợi ý', exact: true })),
      ])
      const submitted = recommendationResponse.request().postDataJSON()
      assert.equal(submitted.analysisId, analysis.analysisId)
      assert.equal(Object.hasOwn(submitted, 'valence'), false)
      const recommendation = await recommendationResponse.json()
      assert.equal(recommendationResponse.status(), 200)
      assert.ok(recommendation.items.length)
      const content = recommendation.items[0].content
      const [detailResponse] = await Promise.all([
        page.waitForResponse(r => /\/api\/(Movies|Music)\//.test(r.url()) && r.request().method() === 'GET'),
        clickVisible(page.getByText(new RegExp('^' + content.tieuDe.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))),
      ])
      assert.equal(detailResponse.status(), 200)
      await clickVisible(page.getByRole('button', { name: 'Đánh giá nội dung', exact: true }))
      await page.getByRole('button', { name: '5 sao', exact: true }).click()
      await enter(page.getByRole('textbox').last(), 'Synthetic browser integration feedback; not an NLP training label.')
      const [rating] = await Promise.all([
        page.waitForResponse(r => r.url().endsWith('/api/History/ratings') && r.request().method() === 'POST'),
        page.getByRole('button', { name: 'Gửi đánh giá', exact: true }).click(),
      ])
      assert.equal(rating.status(), 200)
      assert.equal(rating.request().postDataJSON().recommendationId, recommendation.recommendationId)
      await page.getByRole('button', { name: /^(Back|Quay lại)$/ }).click()
      // Pop animations block taps briefly. An already visited History tab refreshes
      // on historyRevision before navigation, so reuse responses actually fetched by Flutter.
      await page.waitForTimeout(450)
      await nav('Lịch sử')
      await page.getByText('Tâm trạng và gợi ý', { exact: true }).waitFor()
      for (let attempt=0; attempt<100 && !histories.some(h=>h.items.some(item=>item.recommendationId===recommendation.recommendationId)); attempt++) {
        await page.waitForTimeout(100)
      }
      assert.ok(histories.some(h=>h.items.some(item=>item.recommendationId===recommendation.recommendationId)))
      records.push({ language, modelVersion: analysis.modelVersion, recommendationId: recommendation.recommendationId })
    }
    await page.screenshot({ path: folder + '/history.png', fullPage: true })
    assert.deepEqual(errors, [])
    fs.writeFileSync(folder + '/result.json', JSON.stringify({ status: 'passed', records, scope: 'Real Flutter Web, backend and two NLP models; external media playback excluded.' }, null, 2))
    console.log('PASS: Flutter Web VI/EN → actual model → receipt confirmation → details → rating → history.')
  } catch (error) {
    await page.screenshot({ path: folder + '/failure.png', fullPage: true }).catch(() => {})
    const labels = await page.locator('[aria-label]').evaluateAll(nodes => nodes.map(n => n.getAttribute('aria-label')))
    fs.writeFileSync(folder + '/failure-editors.json', JSON.stringify(await page.locator('input,textarea').evaluateAll(nodes=>nodes.map(n=>({tag:n.tagName,role:n.getAttribute('data-semantics-role'),parent:n.parentElement.outerHTML.slice(0,500),bounds:n.getBoundingClientRect().toJSON()}))),null,2))
    fs.writeFileSync(folder + '/failure-dom.txt', JSON.stringify({ labels, text: await page.locator('body').innerText(), errors }, null, 2))
    throw error
  }
} finally { await browser.close() }
