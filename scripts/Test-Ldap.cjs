const assert = require('node:assert/strict');
const { chromium } = require('../artifacts/qa-browser/playwright');

const baseURL = 'https://localhost:7566';
const directoryPassword = 'LocalLdapDemo!7565';
const localPassword = 'LocalTeamManagerOnly!7566';

async function prepareAccounts(browser) {
    const page = await browser.newPage({ baseURL });
    try {
        await page.goto('/Login');
        const local = page.locator('form').filter({ has: page.locator('#EmployeeNumber') });
        await local.locator('#EmployeeNumber').fill('admin');
        await local.locator('#Password').fill('admin');
        await local.locator('button').click();
        await page.goto('/Users');
        for (const employee of ['ldap-demo', 'ldap-unlinked', 'ldap-inactive']) {
            const row = page.getByRole('row').filter({ hasText: employee });
            if (await row.count()) continue;
            const create = page.locator('form[action*="handler=Create"]');
            await create.locator('[name=employee]').fill(employee);
            await create.locator('[name=name]').fill(employee);
            await create.locator('[name=password]').fill(localPassword);
            await create.locator('button').click();
            assert.equal(await row.count(), 1);
        }
        await page.getByRole('row').filter({ hasText: 'ldap-inactive' }).getByRole('link').click();
        const deactivate = page.locator('button[value=deactivate][name=accountAction]');
        if (await deactivate.count()) await deactivate.click();
        assert.ok(await page.locator('button[value=activate][name=accountAction]').isVisible());
    } finally {
        await page.context().close();
    }
}

async function signIn(browser, locale, employee, password, accepted, endpoint = baseURL) {
    const page = await browser.newPage({ baseURL: endpoint, locale });
    try {
        const english = locale === 'en-US';
        await page.goto('/Login');
        await page.getByRole('link', { name: english ? 'LDAP sign-in' : 'LDAP prijava', exact: true }).click();
        await page.getByLabel(english ? 'Employee number' : 'Broj radnika').fill(employee);
        await page.getByLabel(english ? 'Directory password' : 'Lozinka direktorija').fill(password);
        await page.getByRole('button', { name: english ? 'Sign in with LDAP' : 'Prijavi se putem LDAP-a' }).click();
        if (accepted) {
            assert.equal(new URL(page.url()).pathname, '/Dashboard');
            assert.ok(await page.locator('nav span').getByText(employee, { exact: true }).isVisible());
            await page.goto('/Workflow');
            assert.equal(new URL(page.url()).pathname, '/Denied');
            await page.locator('form[action="/Logout"] button').click();
        } else {
            assert.equal(new URL(page.url()).pathname, '/LdapLogin');
            assert.match(await page.getByRole('alert').innerText(),
                english ? /LDAP sign-in failed/ : /LDAP prijava nije uspjela/);
        }
        await page.goto('/Dashboard');
        assert.equal(new URL(page.url()).pathname, '/Login');
        console.log(`PASS: ${locale} ${employee} ${accepted ? 'LDAPS session and logout' : 'no local session'} on ${endpoint}`);
    } finally {
        await page.context().close();
    }
}

(async () => {
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
        await prepareAccounts(browser);
        await signIn(browser, 'hr-HR', 'ldap-demo', directoryPassword, true);
        await signIn(browser, 'en-US', 'ldap-demo', directoryPassword, true);
        await signIn(browser, 'en-US', 'ldap-demo', localPassword, false);
        await signIn(browser, 'en-US', 'ldap-unlinked', directoryPassword, false);
        await signIn(browser, 'hr-HR', 'ldap-inactive', directoryPassword, false);
        if (process.argv.includes('--untrusted-certificate')) {
            await signIn(browser, 'en-US', 'ldap-demo', directoryPassword, false, 'https://localhost:7567');
        }
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
