const assert = require('node:assert/strict');
const { chromium } = require('../artifacts/qa-browser/playwright');

const baseURL = 'https://localhost:7570';
const demoTitle = 'Provjeriti Oracle vezu';
const readerName = 'oracle-qa-reader';
const readerPassword = 'OracleOnlyLocalQA!7570';

async function signIn(page, employee, password) {
    await page.goto('/Login');
    await page.locator('#EmployeeNumber').fill(employee);
    await page.locator('#Password').fill(password);
    await page.locator('form').filter({ has: page.locator('#EmployeeNumber') }).locator('button').click();
    assert.notEqual(new URL(page.url()).pathname, '/Login');
}

async function language(page, value) {
    await page.goto('/Profile');
    const form = page.locator('form[action*="handler=Language"]');
    await form.locator('select[name=language]').selectOption(value);
    await form.locator('button').click();
}

(async () => {
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
        const admin = await browser.newPage({ baseURL });
        assert.equal((await admin.goto('/health/ready')).status(), 200);
        await admin.goto('/OracleOverview');
        assert.equal(new URL(admin.url()).pathname, '/Login');
        await signIn(admin, 'admin', 'admin');

        await language(admin, 'hr');
        await admin.goto('/OracleOverview');
        await admin.getByRole('heading', { name: 'Oracle pregled zadataka' }).waitFor();
        const row = admin.getByRole('row').filter({ hasText: demoTitle });
        const status = row.getByLabel('Status zadatka ' + demoTitle);
        const initial = await status.inputValue();
        const next = initial === 'active' ? 'done' : 'active';
        const revision = await row.locator('input[name=revision]').getAttribute('value');
        await status.selectOption(next);
        await row.getByRole('button', { name: 'Spremi status' }).click();
        assert.equal(await status.inputValue(), next);

        await language(admin, 'en');
        await admin.goto('/OracleOverview');
        await admin.getByRole('heading', { name: 'Oracle task overview' }).waitFor();
        const englishStatus = row.getByLabel('Status for task ' + demoTitle);
        assert.equal(await englishStatus.inputValue(), next);
        assert.deepEqual(await englishStatus.locator('option').allTextContents(), ['New', 'In progress', 'Done']);
        await row.locator('input[name=revision]').evaluate((element, value) => { element.value = value; }, revision);
        await englishStatus.selectOption(initial);
        const conflict = admin.waitForResponse(response =>
            response.request().method() === 'POST' && response.url().includes('/OracleOverview?handler=Status'));
        await row.getByRole('button', { name: 'Save status' }).click();
        assert.equal((await conflict).status(), 409);
        assert.match(await admin.getByRole('alert').innerText(), /The task changed/);
        assert.equal(await englishStatus.inputValue(), next);

        await admin.goto('/Users');
        const existing = admin.getByRole('row').filter({ hasText: readerName });
        if (!(await existing.count())) {
            const create = admin.locator('form[action*="handler=Create"]');
            await create.locator('[name=employee]').fill(readerName);
            await create.locator('[name=name]').fill(readerName);
            await create.locator('[name=password]').fill(readerPassword);
            await create.locator('button').click();
        }
        const reader = await browser.newPage({ baseURL });
        await signIn(reader, readerName, readerPassword);
        await reader.goto('/OracleOverview');
        assert.equal(new URL(reader.url()).pathname, '/Denied');
        assert.equal(await reader.locator('a[href="/OracleOverview"]').count(), 0);
        console.log(`PASS: Oracle read/write HR/EN, stale update blocked, administrator access; stored status: ${next}.`);
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
