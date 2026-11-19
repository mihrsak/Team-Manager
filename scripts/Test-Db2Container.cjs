const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const { chromium } = require('../artifacts/qa-browser/playwright');
const baseURL = 'https://localhost:7591';

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ baseURL });
        await page.goto('/Login');
        await page.locator('#EmployeeNumber').fill('admin');
        await page.locator('#Password').fill('admin');
        await page.locator('form[method="post"] button').last().click();
        assert.notEqual(new URL(page.url()).pathname, '/Login');
        assert.equal(new URL(page.url()).origin, baseURL);
        assert.ok((await page.context().cookies()).some(cookie => cookie.secure && cookie.httpOnly));
        assert.equal((await page.request.get('/Projects')).status(), 200);
        execFileSync('podman', ['restart', 'team-manager-db2-web-qa'], { stdio: 'pipe', timeout: 30000 });
        let ready = false;
        for (let attempt = 0; attempt < 40; attempt++) {
            try { if ((await page.request.get('/health/ready')).status() === 200) { ready = true; break; } } catch { }
            await new Promise(resolve => setTimeout(resolve, 250));
        }
        assert.ok(ready, 'Container readiness after restart');
        const projects = await page.request.get('/Projects');
        assert.equal(projects.status(), 200);
        assert.equal(new URL(projects.url()).pathname, '/Projects', 'Authentication session survived restart');
        console.log('PASS: Linux Db2 through HTTPS proxy, secure session cookie, authenticated project view and session after container restart.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
