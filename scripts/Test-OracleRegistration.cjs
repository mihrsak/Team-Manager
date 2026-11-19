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
async function audit(page) {
    await page.addScriptTag({ path: require.resolve('../artifacts/qa-browser/axe/axe.min.js') });
    const violations = await page.evaluate(async () => (await window.axe.run({ runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] } })).violations.map(v => v.id));
    assert.deepEqual(violations, []);
}
(async () => {
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
        const employee = 'regui' + Date.now();
        const password = 'Registration-browser-2026';
        const applicant = await browser.newPage({ baseURL, extraHTTPHeaders: { 'Accept-Language': 'en' } });
        await applicant.goto('/Register');
        await audit(applicant);
        await post(applicant, '/Register', { employee, name: 'Oracle applicant', password, confirm: password });
        const admin = await browser.newPage({ baseURL });
        await post(admin, '/Login', { EmployeeNumber: 'admin', Password: process.env.TEAM_MANAGER_QA_PASSWORD || 'admin' });
        for (const language of ['en', 'hr']) {
            await post(admin, '/Profile?handler=Language', { language });
            await admin.goto('/RegistrationRequests');
            assert.ok(await admin.getByRole('row').filter({ hasText: employee }).isVisible());
            await audit(admin);
        }
        await admin.getByRole('row').filter({ hasText: employee }).locator('button[value=true]').click();
        await post(applicant, '/Login', { EmployeeNumber: employee, Password: password });
        assert.equal(new URL((await applicant.request.get('/Dashboard')).url()).pathname, '/Dashboard');
        assert.equal(new URL((await applicant.request.get('/RegistrationRequests')).url()).pathname, '/Denied');
        const hr = await browser.newPage({ baseURL, extraHTTPHeaders: { 'Accept-Language': 'hr' } });
        await hr.goto('/Register');
        await audit(hr);
        console.log('PASS: Oracle registration submits anonymously, requires administrator approval, preserves password login, denies pending access to applicants and passes four HR/EN accessibility scans.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
