const assert = require('node:assert/strict');
const path = require('node:path');
const dependencies = path.resolve(__dirname, '../artifacts/qa-browser');
const { chromium } = require(path.join(dependencies, 'playwright'));
const axe = path.join(dependencies, 'axe/axe.min.js');
const baseURL = process.env.TEAM_MANAGER_QA_URL || 'https://localhost:7560';
const suffix = Date.now();
const reader = 'browser-' + suffix;
const password = 'Browser-QA-only-123';
const privateTitle = 'Browser private ' + suffix;
const title = 'Browser issue ' + suffix;
const violations = [];

async function login(page, employee, password) {
    await page.goto('/Login');
    await page.locator('#EmployeeNumber').fill(employee);
    await page.locator('#Password').fill(password);
    await page.locator('form[method="post"] button').last().click();
    assert.notEqual(new URL(page.url()).pathname, '/Login');
}

async function expectHidden(context, route) {
    const page = await context.newPage();
    try {
        const response = page.waitForResponse(response => response.url() === new URL(route, baseURL).href);
        await page.goto(route).catch(error => {
            if (!error.message.includes('ERR_HTTP_RESPONSE_CODE_FAILURE')) throw error;
        });
        assert.equal((await response).status(), 404);
    } finally {
        await page.close();
    }
}

async function audit(page, route, language) {
    const response = await page.goto(route);
    assert.equal(response.status(), 200);
    assert.equal(await page.locator('html').getAttribute('lang'), language);
    await page.addScriptTag({ path: axe });
    const failures = await page.evaluate(async () => {
        const results = await window.axe.run({ runOnly: {
            type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']
        } });
        return results.violations.map(rule => ({
            rule: rule.id, targets: rule.nodes.map(node => node.target.join(' '))
        }));
    });
    violations.push(...failures.map(failure => ({ route, language, ...failure })));
}

async function checkLoginError(page, language) {
    await page.goto('/Login');
    await page.keyboard.press('Tab');
    assert.equal(await page.locator(':focus').getAttribute('href'), '#main-content');
    await page.keyboard.press('Enter');
    await page.keyboard.press('Tab');
    assert.equal(await page.locator(':focus').getAttribute('id'), 'EmployeeNumber');
    await page.keyboard.type('invalid-' + suffix);
    await page.keyboard.press('Tab');
    assert.equal(await page.locator(':focus').getAttribute('id'), 'Password');
    await page.keyboard.type('invalid-password');
    await page.keyboard.press('Enter');
    const error = page.getByRole('alert');
    assert.match(await error.innerText(), language === 'en' ? /Sign-in failed/ : /Neispravna prijava/);
    assert.equal(await page.locator(':focus').getAttribute('id'), 'sign-in-errors');
}

async function addIssue(page, title, privateIssue = false) {
    await page.goto('/Issue?projectId=1');
    const form = page.locator('form').filter({ has: page.locator('#Input_Title') });
    await form.locator('#Input_Title').fill(title);
    await form.locator('#Input_Type').selectOption('Zadatak');
    await form.locator('#Input_Priority').selectOption('Normalan');
    await form.locator('#Input_Status').selectOption('Novo');
    if (privateIssue) await form.locator('input[type=checkbox][name="Input.IsPrivate"]').check();
    await form.locator('button').last().click();
    assert.match(page.url(), /[?&]id=\d+/i, await page.locator('main').innerText());
    return new URL(page.url()).pathname + new URL(page.url()).search;
}

async function run() {
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
        const admin = await browser.newContext({ baseURL, locale: 'hr-HR' });
        const page = await admin.newPage();
        await audit(page, '/Login', 'hr');
        await checkLoginError(page, 'hr');
        const english = await browser.newContext({ baseURL, locale: 'en-US' });
        const englishLogin = await english.newPage();
        await audit(englishLogin, '/Login', 'en');
        await checkLoginError(englishLogin, 'en');
        await englishLogin.close();
        await login(page, 'admin', 'admin');
        const issue = await addIssue(page, title);
        const privateIssue = await addIssue(page, privateTitle, true);
        const comment = page.locator('form[action*="handler=Comment"]');
        await comment.locator('textarea').fill('Browser comment ' + suffix);
        await comment.locator('button').click();
        assert.ok(await page.getByText('Browser comment ' + suffix, { exact: true }).isVisible());
        const time = page.locator('form[action*="handler=Time"]');
        await time.locator('input[name=hours]').fill('1.5');
        await time.locator('input[name=activity]').fill('Browser time ' + suffix);
        await time.locator('button').click();
        assert.ok(await page.getByText('Browser time ' + suffix, { exact: false }).first().isVisible());
        await page.goto('/Users');
        const create = page.locator('form[action*="handler=Create"]');
        await create.locator('[name=employee]').fill(reader);
        await create.locator('[name=name]').fill(reader);
        await create.locator('[name=password]').fill(password);
        await create.locator('button').click();
        const profile = await page.getByRole('row').filter({ hasText: reader }).getByRole('link').getAttribute('href');
        const member = await english.newPage();
        await login(member, reader, password);
        await expectHidden(english, issue);
        await expectHidden(english, privateIssue);
        await expectHidden(english, '/Roadmap?projectId=1');
        await page.goto(profile);
        const membership = page.locator('form[action*="handler=Membership"]').filter({ has: page.locator('input[value=project]') });
        await membership.locator('select[name=scopeId]').selectOption('1');
        await membership.locator('select[name=role]').selectOption('Developer');
        await membership.locator('button').first().click();
        assert.equal((await member.goto(issue)).status(), 200);
        await expectHidden(english, privateIssue);
        await member.goto('/Workflow');
        assert.equal(new URL(member.url()).pathname.toLowerCase(), '/denied');
        console.log('PASS: issue creation, comment, time, private project, membership, private issue and global permission.');
        await page.goto('/ProjectNews?projectId=1');
        const publication = page.locator('form[action*="handler=Publish"]');
        await publication.locator('[name=title]').fill('Browser news ' + suffix);
        await publication.locator('[name=body]').fill('HR/EN news accessibility čćž');
        await publication.locator('button').click();
        const newsArticle = new URL(page.url()).pathname + new URL(page.url()).search;
        assert.ok(new URL(page.url()).searchParams.get('newsId'));
        await page.goto('/ProjectDocuments?projectId=1');
        const documentForm = page.locator('form[action*="handler=Add"]');
        await documentForm.locator('[name=title]').fill('Browser document ' + suffix);
        await documentForm.locator('[name=description]').fill('HR/EN document accessibility čćž');
        await documentForm.locator('[name=attachment]').setInputFiles({ name: 'browser-document.bin', mimeType: 'application/octet-stream', buffer: Buffer.from([0, 1, 255]) });
        await documentForm.locator('button').click();
        assert.ok(await page.getByRole('heading', { name: 'Browser document ' + suffix, exact: true }).isVisible());
        await page.goto('/Wiki?projectId=1');
        const wikiForm = page.locator('form[action*="handler=Save"]');
        await wikiForm.locator('[name=title]').fill('Browser wiki ' + suffix);
        await wikiForm.locator('[name=body]').fill('Wiki first čćž');
        await wikiForm.locator('button').click();
        const wikiArticle = new URL(page.url()).pathname + new URL(page.url()).search;
        assert.ok(new URL(page.url()).searchParams.get('wikiId'));
        await page.locator('form[action*="handler=Save"] [name=body]').fill('Wiki second čćž');
        await page.locator('form[action*="handler=Save"] button').click();
        for (const language of ['hr', 'en']) {
            await page.goto('/Profile');
            const form = page.locator('form[action*="handler=Language"]');
            await form.locator('[name=language]').selectOption(language);
            await form.locator('button').click();
            for (const route of ['/Dashboard', '/Projects', '/Issues', issue, '/Search', '/Calendar', '/TimeReport', '/Profile', '/ProjectNews?projectId=1', newsArticle, '/ProjectDocuments?projectId=1', '/Wiki?projectId=1', wikiArticle, wikiArticle + '&revision=2&compare=1']) {
                await audit(page, route, language);
            }
            await page.goto('/Search');
            await page.setViewportSize({ width: 320, height: 720 });
            assert.ok(await page.locator('main .card').evaluate(card => {
                const right = card.getBoundingClientRect().right;
                return [...card.querySelectorAll('input, select')].every(field => field.getBoundingClientRect().right < right);
            }), 'Search controls fit within their card at 320px');
            await page.setViewportSize({ width: 1280, height: 720 });
            await page.getByLabel(language === 'en' ? 'Module' : 'Modul').selectOption('issues');
            await page.locator('#Query').fill(title);
            await page.locator('main form button').click();
            assert.ok(await page.getByRole('link', { name: title, exact: true }).isVisible());
            await page.goto('/Dashboard');
            await page.keyboard.press('Tab');
            assert.equal(await page.locator(':focus').getAttribute('href'), '#main-content');
            await page.keyboard.press('Enter');
            assert.equal(await page.locator(':focus').getAttribute('id'), 'main-content');
        }
        await page.locator('form[action="/Logout"] button').click();
        await page.goto('/Issues');
        assert.equal(new URL(page.url()).pathname, '/Login');
        assert.deepEqual(violations, []);
        console.log('PASS: 30 WCAG A/AA scans, keyboard skip link, locale switching and logout.');
    } finally {
        if (violations.length) console.error(JSON.stringify(violations, null, 2));
        await browser.close();
    }
}

run().catch(error => { console.error(error); process.exitCode = 1; });
