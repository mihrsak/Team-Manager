const assert = require('node:assert/strict');
const { chromium } = require('../artifacts/qa-browser/playwright');
const baseURL = process.env.TEAM_MANAGER_ORACLE_URL || 'https://localhost:7589';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(baseURL).hostname), 'Local test instance required');
const suffix = Date.now().toString();
const password = 'Local-profile-QA-2026';
async function post(page, path, fields) {
    const get = await page.request.get(path); assert.equal(get.status(), 200, path);
    const token = (await get.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1]; assert.ok(token);
    const response = await page.request.post(path, { form: { ...fields, __RequestVerificationToken: token } });
    assert.equal(response.status(), 200, path); return response;
}
async function login(browser, employee, secret) {
    const page = await browser.newPage({ baseURL });
    await post(page, '/Login', { EmployeeNumber: employee, Password: secret });
    assert.notEqual(new URL((await page.request.get('/Dashboard')).url()).pathname, '/Login'); return page;
}
(async () => {
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
        const admin = await login(browser, 'admin', process.env.TEAM_MANAGER_QA_PASSWORD || 'admin');
        async function account(prefix) {
            const employee = prefix + suffix;
            await post(admin, '/Users?handler=Create', { employee, name: employee, password, job: 'Profile privacy fixture' });
            await admin.goto('/Users');
            const path = await admin.getByRole('row').filter({ hasText: employee }).getByRole('link').getAttribute('href');
            assert.ok(path); return { path, page: await login(browser, employee, password) };
        }
        const subject = await account('subject'); const viewer = await account('viewer');
        const accountAdmin = await account('acctadmin');
        const secretRole = 'PrivateRole' + suffix;
        await post(admin, '/Roles', { name: secretRole, description: 'Private project fixture', create: 'true' });
        await post(admin, '/Roles', { name: 'Accounts' + suffix, description: 'Account administration', create: 'true', rights: 'users.manage' });
        await post(admin, accountAdmin.path + '&handler=GlobalRole', { role: 'Accounts' + suffix });
        async function project(name, parent = '', inherit = false, isPublic = false) {
            const response = await post(admin, '/Project', { 'Input.Name': name, 'Input.Description': 'Profile privacy', 'Input.State': 'Active', 'Input.IsPublic': String(isPublic), 'Input.ParentId': String(parent), 'Input.InheritParentRoles': String(inherit), 'Input.Revision': '0' });
            const id = new URL(response.url()).searchParams.get('id'); assert.ok(Number(id) > 0); return id;
        }
        const privateName = 'PRIVATE-PROFILE-' + suffix, publicName = 'PUBLIC-PROFILE-' + suffix;
        const parent = await project('Parent profile ' + suffix);
        const child = await project(privateName, parent, true);
        const publicId = await project(publicName, '', false, true);
        for (const id of [child, publicId]) await post(admin, subject.path + '&handler=Membership', { kind: 'project', scopeId: id, role: secretRole });
        await post(admin, subject.path + '&handler=Membership', { kind: 'project', scopeId: child, role: 'Moderator' });
        for (const language of ['en', 'hr']) {
            await post(viewer.page, '/Profile?handler=Language', { language });
            await viewer.page.goto(subject.path);
            assert.equal(await viewer.page.locator('html').getAttribute('lang'), language);
            const html = await viewer.page.content();
            assert.ok(!html.includes(privateName), 'Private name leaked into HTML');
            // Role appears only for the public project; the hidden private additional role must not.
            assert.equal(await viewer.page.getByText('Moderator', { exact: true }).count(), 0);
            assert.ok(await viewer.page.getByText(publicName + ' ·', { exact: false }).count());
            assert.equal((await viewer.page.request.get('/Project?id=' + child)).status(), 404);
        }
        await subject.page.goto('/Profile'); assert.ok((await subject.page.locator('main').innerText()).includes(privateName), 'Self membership missing');
        await accountAdmin.page.goto(subject.path); assert.ok((await accountAdmin.page.locator('main').innerText()).includes(privateName), 'Account admin membership missing');
        await admin.goto(subject.path); assert.ok((await admin.locator('main').innerText()).includes(privateName));
        await post(admin, viewer.path + '&handler=Membership', { kind: 'project', scopeId: parent, role: 'Developer' });
        await viewer.page.goto(subject.path); assert.ok((await viewer.page.locator('main').innerText()).includes(privateName), 'Inherited project visibility missing');
        assert.ok(await viewer.page.getByText('Moderator', { exact: true }).count(), 'Additional project role missing for authorized viewer');
        await post(admin, viewer.path + '&handler=Remove', { kind: 'project', scopeId: parent });
        await viewer.page.goto(subject.path); assert.ok(!(await viewer.page.content()).includes(privateName), 'Revoked project name remained visible');
        assert.equal(await viewer.page.getByText('Moderator', { exact: true }).count(), 0, 'Revoked role remained visible');
        await post(admin, '/Profile?handler=Language', { language: 'hr' });
        console.log('PASS: HR/EN profile HTML hides private names and roles; public/self/admin and inherited access remain valid; revocation applies immediately.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
