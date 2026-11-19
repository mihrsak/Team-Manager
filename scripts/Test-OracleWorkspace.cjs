const assert = require('node:assert/strict');
const { chromium } = require('../artifacts/qa-browser/playwright');

const baseURL = process.env.TEAM_MANAGER_ORACLE_URL || 'https://localhost:7572';
const suffix = Date.now();
const password = 'Oracle-QA-password-2026';

async function post(page, path, fields, expected = 200) {
    const get = await page.request.get(path);
    assert.equal(get.status(), 200, 'Load form ' + path);
    const token = (await get.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
    assert.ok(token, 'Antiforgery token on ' + path);
    const response = await page.request.post(path, {
        form: { ...fields, __RequestVerificationToken: token }
    });
    assert.equal(response.status(), expected, path + '\n' + (await response.text()).slice(-300));
    return response;
}

async function login(browser, employee, password) {
    const page = await browser.newPage({ baseURL });
    await post(page, '/Login', { EmployeeNumber: employee, Password: password });
    const dashboard = await page.request.get('/Dashboard');
    assert.equal(new URL(dashboard.url()).pathname, '/Dashboard');
    return page;
}

async function project(page, name, parent = '', inherit = false) {
    const result = await post(page, '/Project', {
        'Input.Name': name, 'Input.Description': 'Oracle workspace QA',
        'Input.State': 'Active', 'Input.ParentId': parent,
        'Input.InheritParentRoles': String(inherit), 'Input.IsPublic': 'false', 'Input.Revision': '0'
    });
    const id = Number(new URL(result.url()).searchParams.get('id'));
    assert.ok(id > 0);
    return id;
}

async function issue(page, projectId, title, isPrivate = false) {
    const result = await post(page, '/Issue?projectId=' + projectId + '&handler=Save', {
        'Input.Title': title, 'Input.Description': 'Oracle transaction QA',
        'Input.Type': 'Zadatak', 'Input.Status': 'Novo', 'Input.Priority': 'Normalan',
        'Input.IsPrivate': String(isPrivate), 'Input.Revision': '0'
    });
    const id = Number(new URL(result.url()).searchParams.get('id'));
    assert.ok(id > 0);
    return id;
}

async function checkStatus(page, path, status) {
    assert.equal((await page.request.get(path)).status(), status, path);
}

async function run() {
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
        const admin = await login(browser, 'admin', process.env.TEAM_MANAGER_QA_PASSWORD || 'admin');
        const employee = 'ora' + suffix;
        await post(admin, '/Users?handler=Create', {
            employee, name: employee, password, job: 'Oracle tester'
        });
        await admin.goto('/Users');
        const profile = await admin.getByRole('row').filter({ hasText: employee })
            .getByRole('link').getAttribute('href');
        const parent = await project(admin, 'Oracle parent ' + suffix);
        const child = await project(admin, 'Oracle child ' + suffix, parent, true);
        const publicIssue = await issue(admin, child, 'Oracle visible ' + suffix);
        const privateIssue = await issue(admin, child, 'Oracle private ' + suffix, true);
        const reader = await login(browser, employee, password);
        await checkStatus(reader, '/Issue?id=' + publicIssue, 404);
        await post(admin, profile + '&handler=Membership', {
            kind: 'project', scopeId: String(parent), role: 'Developer'
        });
        await checkStatus(reader, '/Issue?id=' + publicIssue, 200);
        await checkStatus(reader, '/Issue?id=' + privateIssue, 404);
        console.log('PASS: private project, parent role inheritance and private issue visibility.');

        // Both writes carry the same revision. Oracle must commit one edit and reject the other.
        const fields = {
            'Input.Description': 'Atomic concurrent change', 'Input.Type': 'Zadatak',
            'Input.Status': 'Novo', 'Input.Priority': 'Normalan', 'Input.Revision': '1'
        };
        const updates = await Promise.all(['first', 'second'].map(value =>
            post(reader, '/Issue?id=' + publicIssue + '&handler=Save', {
                ...fields, 'Input.Title': 'Oracle ' + value + ' ' + suffix
            })));
        const failures = await Promise.all(updates.map(async response =>
            (await response.text()).includes('class="validation-summary-errors"')));
        assert.equal(failures.filter(Boolean).length, 1);
        await admin.goto('/Issue?id=' + publicIssue);
        assert.equal(await admin.locator('#Input_Revision').inputValue(), '2');
        console.log('PASS: two concurrent edits produce one commit and one stale revision error.');

        await post(reader, '/Issue?id=' + publicIssue + '&handler=Comment', { text: 'Oracle comment ' + suffix });
        await post(reader, '/Issue?id=' + publicIssue + '&handler=Time', {
            date: '2026-11-19', hours: '1.75', note: 'Oracle time', activity: 'Oracle QA'
        });
        await post(reader, '/Issue?id=' + publicIssue + '&handler=PrivateNote', { text: 'Oracle personal ' + suffix });
        await post(reader, '/Issue?id=' + publicIssue + '&handler=Watch', { watch: 'true' });
        await reader.goto('/Issue?id=' + publicIssue);
        assert.ok(await reader.getByText('Oracle comment ' + suffix, { exact: true }).isVisible());
        assert.ok(await reader.getByText('Oracle personal ' + suffix, { exact: false }).isVisible());
        await admin.goto('/Issue?id=' + publicIssue);
        assert.equal(await admin.getByText('Oracle personal ' + suffix, { exact: false }).count(), 0);

        const token = await reader.locator('input[name=__RequestVerificationToken]').first().inputValue();
        const content = Buffer.from('Oracle binary attachment \u0000 \u010d \ud83d\ude80', 'utf8');
        const upload = await reader.request.post('/Issue?id=' + publicIssue + '&handler=Upload', {
            multipart: {
                __RequestVerificationToken: token,
                attachment: { name: 'oracle-qa.bin', mimeType: 'application/octet-stream', buffer: content }
            }
        });
        assert.equal(upload.status(), 200);
        await reader.goto('/Issue?id=' + publicIssue);
        const downloadPath = await reader.getByRole('link', { name: 'oracle-qa.bin' }).getAttribute('href');
        assert.deepEqual(await (await reader.request.get(downloadPath)).body(), content);
        console.log('PASS: comment, time, private note, watcher and byte-identical attachment.');

        await post(admin, '/Issue?id=' + privateIssue + '&handler=Link', {
            otherId: String(publicIssue), relation: 'blocks'
        });
        await reader.goto('/Issue?id=' + publicIssue);
        assert.equal(await reader.getByText('Oracle private ' + suffix, { exact: false }).count(), 0);
        await post(admin, profile + '&handler=Remove', { kind: 'project', scopeId: String(parent) });
        await checkStatus(reader, '/Issue?id=' + publicIssue, 404);
        await checkStatus(reader, downloadPath, 404);
        await post(reader, '/Profile?handler=Language', { language: 'en' });
        await reader.goto('/Workflow');
        assert.equal(new URL(reader.url()).pathname, '/Denied');
        console.log('PASS: membership revocation protects direct issue and attachment URLs immediately.');

        const guard = await post(admin, '/Roles', {
            name: 'Administrator', description: 'QA guard', create: 'false'
        });
        assert.ok((await guard.text()).includes('class="validation-summary-errors"'));
        await admin.goto('/Roles');
        assert.equal(new URL(admin.url()).pathname, '/Roles');
        console.log('PASS: attempted removal of the last administrator rolls back.');

        await post(admin, '/Roadmap?projectId=' + child + '&handler=Save', {
            itemId: '0', revision: '0', kind: 'milestone',
            name: 'Oracle milestone ' + suffix, description: 'Oracle milestone detail', targetDate: '2026-12-15'
        });
        await admin.goto('/Roadmap?projectId=' + child);
        const milestone = await admin.locator('section').filter({
            has: admin.getByRole('heading', { name: 'Oracle milestone ' + suffix, exact: true })
        }).locator('input[name=itemId]').inputValue();
        await admin.goto('/Issue?id=' + publicIssue);
        const issueRevision = await admin.locator('#Input_Revision').inputValue();
        const issueTitle = await admin.locator('#Input_Title').inputValue();
        await post(admin, '/Issue?id=' + publicIssue + '&handler=Save', {
            'Input.Title': issueTitle, 'Input.Description': 'Oracle transaction QA',
            'Input.Type': 'Zadatak', 'Input.Status': 'Novo', 'Input.Priority': 'Normalan',
            'Input.Revision': issueRevision, 'Input.RoadmapItemId': milestone
        });
        await post(admin, '/IssueFields?projectId=' + child + '&handler=Create', {
            name: 'Oracle restricted ' + suffix, managersOnly: 'true'
        });
        await admin.goto('/Issue?id=' + publicIssue);
        const field = admin.locator('form').filter({ has: admin.getByLabel('Oracle restricted ' + suffix) });
        const fieldId = await field.locator('input[name=fieldId]').inputValue();
        const fieldRevision = await field.locator('input[name=revision]').inputValue();
        await post(admin, '/Issue?id=' + publicIssue + '&handler=SetField', {
            fieldId, revision: fieldRevision, fieldValue: 'Oracle preserved value'
        });
        await admin.goto('/Issue?id=' + publicIssue);
        assert.equal(await admin.getByLabel('Oracle restricted ' + suffix).inputValue(), 'Oracle preserved value');
        assert.equal(await admin.locator('#Input_RoadmapItemId').inputValue(), milestone);
        assert.ok(await admin.getByText('Oracle comment ' + suffix, { exact: true }).isVisible());
        const attachmentPath = await admin.getByRole('link', { name: 'oracle-qa.bin' }).getAttribute('href');
        assert.deepEqual(await (await admin.request.get(attachmentPath)).body(), content);
        console.log('PASS: roadmap, restricted field, comment and attachment retained through subsequent edits.');

        await post(admin, '/CustomFields?scope=project&handler=Create', { name: 'Oracle project field ' + suffix });
        await admin.goto('/CustomFields?scope=project&entityId=' + child);
        const custom = admin.locator('form').filter({ has: admin.getByLabel('Oracle project field ' + suffix) });
        await custom.locator('input[name=fieldValue]').fill('Oracle project metadata');
        await custom.locator('button').click();
        assert.equal(await admin.getByLabel('Oracle project field ' + suffix).inputValue(), 'Oracle project metadata');
        const transferRevision = await (async () => {
            await admin.goto('/Issue?id=' + publicIssue);
            return admin.locator('#Input_Revision').inputValue();
        })();
        await post(admin, '/Issue?id=' + publicIssue + '&handler=Transfer', {
            revision: transferRevision, destination: String(parent), move: 'true'
        });
        await admin.goto('/Issue?id=' + publicIssue);
        assert.equal(await admin.locator('#Input_RoadmapItemId').inputValue(), '');
        assert.ok(await admin.getByText('Oracle comment ' + suffix, { exact: true }).isVisible());
        assert.equal(await admin.getByLabel('Oracle restricted ' + suffix).count(), 0);
        assert.deepEqual(await (await admin.request.get(attachmentPath)).body(), content);
        console.log('PASS: project custom field and issue move preserve related data and clear the source roadmap.');

        await checkStatus(admin, '/AdministrationAudit', 200);
        await checkStatus(admin, '/ProjectDocuments?projectId=' + parent, 503);
        console.log('READY: ' + baseURL + '/Projects');
    } finally {
        await browser.close();
    }
}

run().catch(error => { console.error(error); process.exitCode = 1; });
