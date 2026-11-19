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
        const admin = await browser.newPage({ baseURL });
        await post(admin, '/Login', { EmployeeNumber: 'admin', Password: process.env.TEAM_MANAGER_QA_PASSWORD || 'admin' });
        const group = 'Browser group ' + Date.now();
        await post(admin, '/Groups?handler=Create', { name: group });
        await admin.goto('/Groups');
        const section = admin.locator('section').filter({ has: admin.getByRole('heading', { name: group, exact: true }) });
        const groupId = new URL(await section.locator('form').first().getAttribute('action'), baseURL).searchParams.get('groupId');
        assert.ok(Number(groupId) > 0);
        await post(admin, '/Groups?handler=Role&groupId=' + groupId, { role: 'Developer' });
        for (const language of ['en', 'hr']) {
            await post(admin, '/Profile?handler=Language', { language });
            await admin.goto('/Groups');
            assert.equal(await admin.locator('html').getAttribute('lang'), language);
            await admin.addScriptTag({ path: require.resolve('../artifacts/qa-browser/axe/axe.min.js') });
            const violations = await admin.evaluate(async () => (await window.axe.run({ runOnly: { type: 'tag', values: ['wcag2a','wcag2aa','wcag21a','wcag21aa'] } })).violations.map(v => v.id));
            assert.deepEqual(violations, []);
        }
        await post(admin, '/Groups?handler=Role&groupId=' + groupId, { role: 'Developer', remove: 'true' });
        console.log('PASS: Group creation, role assignment/removal and two HR/EN accessibility scans.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
