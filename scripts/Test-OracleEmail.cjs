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
        const employee = 'emailui' + Date.now();
        const password = 'Email-browser-2026';
        await post(admin, '/Users?handler=Create', { employee, name: 'Email UI test', job: '', password });
        const page = await browser.newPage({ baseURL });
        await post(page, '/Login', { EmployeeNumber: employee, Password: password });
        await page.goto('/EmailNotifications');
        const initial = {
            address: await page.locator('[name=address]').inputValue(),
            watched: await page.locator('[name=sendWatchedIssues]').isChecked(),
            reminders: await page.locator('[name=sendDueReminders]').isChecked()
        };
        try {
            await post(page, '/EmailNotifications', { address: 'local-preview@example.test', sendWatchedIssues: 'true', sendDueReminders: 'true' });
            for (const language of ['en', 'hr']) {
                await post(page, '/Profile?handler=Language', { language });
                await page.goto('/EmailNotifications');
                assert.equal(await page.locator('[name=address]').inputValue(), 'local-preview@example.test');
                assert.equal(await page.locator('[name=sendWatchedIssues]').isChecked(), true);
                await page.addScriptTag({ path: require.resolve('../artifacts/qa-browser/axe/axe.min.js') });
                const violations = await page.evaluate(async () => (await window.axe.run({ runOnly: { type: 'tag', values: ['wcag2a','wcag2aa','wcag21a','wcag21aa'] } })).violations.map(v => v.id));
                assert.deepEqual(violations, []);
            }
        } finally { await post(page, '/EmailNotifications', { address: initial.address, ...(initial.watched ? { sendWatchedIssues: 'true' } : {}), ...(initial.reminders ? { sendDueReminders: 'true' } : {}) }); }
        console.log('PASS: Email preferences persist on Oracle and pass two HR/EN accessibility scans.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
