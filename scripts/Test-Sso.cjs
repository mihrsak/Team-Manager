const assert = require('node:assert/strict');
const { chromium } = require('../artifacts/qa-browser/playwright');

const baseURL = 'https://localhost:7564';
const provider = 'https://localhost:7563';
const localTestPassword = 'TeamManagerQA-password-7564';
const providerTestPassword = 'LocalSsoDemo!7563';

async function prepareAccounts(browser) {
    const context = await browser.newContext({ baseURL });
    try {
        const page = await context.newPage();
        await page.goto('/Login');
        const local = page.locator('form').filter({ has: page.locator('#EmployeeNumber') });
        await local.locator('#EmployeeNumber').fill('admin');
        await local.locator('#Password').fill('admin');
        await local.locator('button').click();
        await page.goto('/Users');
        for (const employee of ['sso-demo', 'sso-unlinked', 'sso-inactive']) {
            const row = page.getByRole('row').filter({ hasText: employee });
            if (await row.count()) continue;
            const create = page.locator('form[action*="handler=Create"]');
            await create.locator('[name=employee]').fill(employee);
            await create.locator('[name=name]').fill(employee);
            await create.locator('[name=password]').fill(localTestPassword);
            await create.locator('button').click();
            assert.equal(await row.count(), 1);
        }
        await page.getByRole('row').filter({ hasText: 'sso-inactive' }).getByRole('link').click();
        const deactivate = page.locator('button[value=deactivate][name=accountAction]');
        if (await deactivate.count()) await deactivate.click();
        assert.ok(await page.locator('button[value=activate][name=accountAction]').isVisible());
    } finally {
        await context.close();
    }
}

async function signIn(browser, locale, employee, accepted) {
    const context = await browser.newContext({ baseURL, locale });
    try {
        const page = await context.newPage();
        const english = locale === 'en-US';
        await page.goto('/Login');
        await page.getByRole('button', {
            name: english ? 'Sign in with SSO' : 'Prijavi se putem SSO-a',
            exact: true
        }).click();
        assert.equal(new URL(page.url()).origin, provider);
        await page.locator('#username').fill(employee);
        await page.locator('#password').fill(providerTestPassword);
        await page.locator('#kc-login').click();
        await page.waitForURL(url => url.origin === baseURL && url.pathname !== '/signin-sso');
        if (accepted) {
            assert.equal(new URL(page.url()).pathname, '/Dashboard');
            assert.ok(await page.locator('nav span').getByText(employee, { exact: true }).isVisible());
            await page.goto('/Workflow');
            assert.equal(new URL(page.url()).pathname, '/Denied');
            await page.locator('form[action="/Logout"] button').click();
            await page.goto('/Dashboard');
            assert.equal(new URL(page.url()).pathname, '/Login');
        } else {
            assert.equal(new URL(page.url()).pathname, '/Login');
            assert.match(await page.getByRole('alert').innerText(),
                english ? /not linked to an active account/ : /nije povezan s aktivnim računom/);
            await page.goto('/Dashboard');
            assert.equal(new URL(page.url()).pathname, '/Login');
        }
        console.log(`PASS: ${locale} ${employee} ${accepted ? 'local session and logout' : 'no local session'}`);
    } finally {
        await context.close();
    }
}

(async () => {
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
        await prepareAccounts(browser);
        await signIn(browser, 'hr-HR', 'sso-demo', true);
        await signIn(browser, 'en-US', 'sso-demo', true);
        await signIn(browser, 'en-US', 'sso-unlinked', false);
        await signIn(browser, 'hr-HR', 'sso-inactive', false);
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
