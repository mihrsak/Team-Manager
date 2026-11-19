const assert = require('node:assert/strict');
const { chromium } = require('../artifacts/qa-browser/playwright');
const baseURL = process.env.TEAM_MANAGER_ORACLE_URL || 'https://localhost:7572';
async function post(page, path, fields) {
    const form = await page.request.get(path);
    assert.equal(form.status(), 200);
    const token = (await form.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
    assert.ok(token);
    return page.request.post(path, { form: { ...fields, __RequestVerificationToken: token } });
}
(async () => {
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
        const page = await browser.newPage({ baseURL });
        await post(page, '/Login', { EmployeeNumber: 'admin', Password: process.env.TEAM_MANAGER_QA_PASSWORD || 'admin' });
        const name = 'API browser ' + Date.now();
        await page.goto('/ApiTokens');
        await page.locator('[name=name]').fill(name);
        await page.locator('form[action*="handler=Create"] button').click();
        const secret = await page.locator('input[readonly]').inputValue();
        assert.match(secret, /^tm_[0-9a-f]{64}$/);
        const anonymous = await browser.newContext({ baseURL });
        try {
            assert.equal((await anonymous.request.get('/api/v1/projects')).status(), 401);
            const response = await anonymous.request.get('/api/v1/projects', { headers: { Authorization: 'Bearer ' + secret } });
            assert.equal(response.status(), 200);
            assert.ok((await response.json()).length > 0);
            assert.equal((await anonymous.request.get('/api/v1/issues/2147483647', { headers: { Authorization: 'Bearer ' + secret } })).status(), 404);
            await page.goto('/ApiTokens');
            assert.equal(await page.locator('input[readonly]').count(), 0);
            const row = page.getByRole('row').filter({ hasText: name });
            await row.locator('button').click();
            assert.equal((await anonymous.request.get('/api/v1/projects', { headers: { Authorization: 'Bearer ' + secret } })).status(), 401);
        } finally { await anonymous.close(); }
        for (const language of ['en', 'hr']) {
            await post(page, '/Profile?handler=Language', { language });
            await page.goto('/ApiTokens');
            assert.equal(await page.locator('html').getAttribute('lang'), language);
            await page.addScriptTag({ path: require.resolve('../artifacts/qa-browser/axe/axe.min.js') });
            const violations = await page.evaluate(async () => (await window.axe.run({ runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] } })).violations.map(v => v.id));
            assert.deepEqual(violations, []);
        }
        console.log('PASS: Oracle token UI shows secret once, REST rejects missing/revoked tokens, applies issue visibility, and passes two HR/EN accessibility scans.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
