const assert = require('node:assert/strict');
const { chromium } = require('../artifacts/qa-browser/playwright');

const baseURL = process.env.TEAM_MANAGER_ORACLE_URL || 'https://localhost:7572';
const suffix = Date.now();
const password = 'Oracle-QA-password-2026';
const decodeNumericEntities = text => text.replace(/&#x([0-9a-f]+);/gi, (_, value) => String.fromCodePoint(parseInt(value, 16))).replace(/&#([0-9]+);/g, (_, value) => String.fromCodePoint(Number(value)));

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
        await post(admin, '/Users?handler=Scope', { kind: 'unit', name: 'Oracle unit ' + suffix });
        await post(admin, '/Users?handler=Scope', { kind: 'team', name: 'Oracle team ' + suffix });
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
        const newsPath = '/ProjectNews?projectId=' + child;
        const published = await post(admin, newsPath + '&handler=Publish', {
            title: 'Oracle novosti / News ' + suffix, body: 'Ž č ć š đ 🚀 <b>plain text</b>'
        });
        const newsId = new URL(published.url()).searchParams.get('newsId');
        assert.ok(Number(newsId) > 0);
        const articlePath = newsPath + '&newsId=' + newsId;
        await checkStatus(reader, articlePath, 200);
        const commented = await post(reader, articlePath + '&handler=Comment', { body: 'Komentar / Comment <script> čćž 🚀' });
        assert.ok((await commented.text()).includes('&lt;script&gt;'));
        const wrongProject = await project(admin, 'Oracle news boundary ' + suffix);
        await checkStatus(admin, '/ProjectNews?projectId=' + wrongProject + '&newsId=' + newsId, 404);
        for (const language of ['en', 'hr']) {
            await post(admin, '/Profile?handler=Language', { language });
            await admin.goto(articlePath);
            assert.ok(await admin.getByRole('heading', { name: language === 'en' ? 'Project news' : 'Projektne novosti', exact: true }).isVisible());
            const invalid = await post(admin, newsPath + '&handler=Publish', { title: 'x'.repeat(151), body: 'Invalid length' });
            assert.ok((await invalid.text()).includes(language === 'en'
                ? 'Enter a title up to 150 characters and text up to 4,000 characters.'
                : 'Unesi naslov do 150 znakova i tekst do 4000 znakova.'));
        }
        console.log('PASS: Oracle news publish/comment, inherited access, project isolation, HTML escaping and HR/EN validation.');
        const documentsPath = '/ProjectDocuments?projectId=' + child;
        const documentBytes = Buffer.from(Array.from({ length: 262144 }, (_, i) => i % 256));
        const documentForm = await admin.request.get(documentsPath);
        const documentToken = (await documentForm.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
        const uploadedDocument = await admin.request.post(documentsPath + '&handler=Add', { multipart: {
            __RequestVerificationToken: documentToken, title: 'Oracle document ' + suffix,
            description: 'Opis <b>čćž 🚀</b>', attachment: { name: 'oracle-document.bin', mimeType: 'application/octet-stream', buffer: documentBytes }
        } });
        assert.equal(uploadedDocument.status(), 200);
        await reader.goto(documentsPath);
        const documentDownload = await reader.getByRole('link', { name: 'oracle-document.bin', exact: true }).getAttribute('href');
        assert.deepEqual(await (await reader.request.get(documentDownload)).body(), documentBytes);
        assert.ok((await reader.content()).includes('&lt;b&gt;'));
        const documentId = new URL(documentDownload, baseURL).searchParams.get('documentId');
        await checkStatus(admin, '/ProjectDocuments?projectId=' + wrongProject + '&handler=Download&documentId=' + documentId, 404);
        for (const language of ['en', 'hr']) {
            await post(admin, '/Profile?handler=Language', { language });
            await admin.goto(documentsPath);
            assert.ok(await admin.getByRole('heading', { name: language === 'en' ? 'Project documents' : 'Dokumenti projekta', exact: true }).isVisible());
            const invalidDocument = await post(admin, documentsPath + '&handler=Add', { title: 'No file', description: '' });
            assert.ok((await invalidDocument.text()).includes(language === 'en'
                ? 'Select a file' : 'Odaberi datoteku'));
        }
        console.log('PASS: Oracle documents upload/download, inherited visibility, byte-identical BLOB, project boundary and HR/EN validation.');
        const wikiPath = '/Wiki?projectId=' + child;
        const wikiCreated = await post(admin, wikiPath + '&handler=Save', { revision: '0', title: 'Oracle wiki ' + suffix, body: 'Prva <b>verzija</b> čćž 🚀' });
        const wikiId = new URL(wikiCreated.url()).searchParams.get('wikiId');
        assert.ok(Number(wikiId) > 0);
        const wikiArticle = wikiPath + '&wikiId=' + wikiId;
        await post(reader, wikiArticle + '&handler=Save', { revision: '1', title: 'Oracle wiki edited ' + suffix, body: 'Second revision čćž 🚀' });
        const firstWiki = await reader.request.get(wikiArticle + '&revision=1');
        assert.equal(firstWiki.status(), 200);
        assert.ok((await firstWiki.text()).includes('Prva &lt;b&gt;verzija&lt;/b&gt;'));
        const comparedWiki = await reader.request.get(wikiArticle + '&revision=2&compare=1');
        assert.ok((await comparedWiki.text()).includes('Second revision'));
        await checkStatus(admin, '/Wiki?projectId=' + wrongProject + '&wikiId=' + wikiId + '&revision=1', 404);
        for (const language of ['en', 'hr']) {
            await post(admin, '/Profile?handler=Language', { language });
            await admin.goto(wikiArticle);
            assert.ok(await admin.getByRole('heading', { name: language === 'en' ? 'Project wiki' : 'Projektni wiki', exact: true }).isVisible());
            const stale = await post(admin, wikiArticle + '&handler=Save', { revision: '1', title: 'Stale', body: 'Must not replace current/history' });
            assert.ok(decodeNumericEntities(await stale.text()).includes(language === 'en' ? 'The page has changed.' : 'Stranica je u međuvremenu izmijenjena.'));
            const duplicate = await post(admin, wikiPath + '&handler=Save', { revision: '0', title: 'Oracle wiki edited ' + suffix, body: 'Duplicate' });
            assert.ok(decodeNumericEntities(await duplicate.text()).includes(language === 'en' ? 'A page with that title already exists in the project.' : 'Stranica s tim naslovom već postoji u projektu.'));
        }
        console.log('PASS: Oracle wiki create/edit, inherited authority, immutable history/compare, project isolation and HR/EN stale/duplicate messages.');
        const forumPath = '/Forum?projectId=' + child;
        const forumCreated = await post(reader, forumPath + '&handler=Create', { title: 'Oracle forum ' + suffix, body: 'Tema <b>čćž 🚀</b>' });
        const topicId = new URL(forumCreated.url()).searchParams.get('topicId');
        assert.ok(Number(topicId) > 0);
        const topicPath = forumPath + '&topicId=' + topicId;
        const forumReplied = await post(reader, topicPath + '&handler=Reply', { body: 'Odgovor <script> čćž 🚀' });
        assert.ok((await forumReplied.text()).includes('&lt;script&gt;'));
        const deniedModeration = await post(reader, topicPath + '&handler=Lock', { locked: 'true' });
        assert.equal(new URL(deniedModeration.url()).pathname, '/Denied');
        await checkStatus(admin, '/Forum?projectId=' + wrongProject + '&topicId=' + topicId, 404);
        for (const language of ['en', 'hr']) {
            await post(admin, '/Profile?handler=Language', { language });
            await admin.goto(topicPath);
            assert.ok(await admin.getByRole('heading', { name: language === 'en' ? 'Project forum' : 'Projektni forum', exact: true }).isVisible());
            const invalidReply = await post(admin, topicPath + '&handler=Reply', { body: '' });
            assert.ok(decodeNumericEntities(await invalidReply.text()).includes(language === 'en' ? 'Enter a reply up to 4,000 characters.' : 'Unesi odgovor do 4000 znakova.'));
            await post(admin, topicPath + '&handler=Lock', { locked: 'true' });
            await admin.goto(forumPath);
            const topicRow = admin.getByRole('listitem').filter({ hasText: 'Oracle forum ' + suffix });
            assert.ok((await topicRow.textContent()).includes(language === 'en' ? 'Locked' : 'Zaključano'));
            const blockedReply = await post(reader, topicPath + '&handler=Reply', { body: 'Locked reply must fail' });
            assert.equal(new URL(blockedReply.url()).pathname, '/Denied');
            await post(admin, topicPath + '&handler=Lock', { locked: 'false' });
        }
        console.log('PASS: Oracle forum inherited create/reply, moderation separation, locking, project isolation, HTML escaping and HR/EN validation/status.');
        const repositoriesPath = '/Repositories?projectId=' + child;
        const repositoryName = 'Oracle repo ' + suffix;
        await post(admin, repositoriesPath + '&handler=Add', { name: repositoryName, url: 'https://example.test/' + suffix + '.git' });
        await admin.goto(repositoriesPath);
        const repositoryRow = admin.getByRole('listitem').filter({ hasText: repositoryName });
        const removeRepositoryPath = await repositoryRow.locator('form').getAttribute('action');
        const repositoryId = new URL(removeRepositoryPath, baseURL).searchParams.get('repositoryId');
        assert.ok(Number(repositoryId) > 0);
        await reader.goto(repositoriesPath);
        assert.ok(await reader.getByRole('link', { name: repositoryName, exact: true }).isVisible());
        const forbiddenRemoval = await post(reader, removeRepositoryPath, {});
        assert.equal(new URL(forbiddenRemoval.url()).pathname, '/Denied');
        const forbiddenHistory = await reader.request.get('/RepositoryHistory?projectId=' + child + '&repositoryId=' + repositoryId);
        assert.equal(new URL(forbiddenHistory.url()).pathname, '/Denied');
        await checkStatus(admin, '/RepositoryHistory?projectId=' + wrongProject + '&repositoryId=' + repositoryId, 404);
        for (const language of ['en', 'hr']) {
            await post(admin, '/Profile?handler=Language', { language });
            const invalidRepo = await post(admin, repositoriesPath + '&handler=Add', { name: 'Invalid', url: 'http://example.test/repo' });
            assert.ok(decodeNumericEntities(await invalidRepo.text()).includes(language === 'en' ? 'Enter a name up to 80 characters' : 'Unesi naziv do 80 znakova'));
            const duplicateRepo = await post(admin, repositoriesPath + '&handler=Add', { name: repositoryName, url: 'https://example.test/duplicate.git' });
            assert.ok(decodeNumericEntities(await duplicateRepo.text()).includes(language === 'en' ? 'That name already exists.' : 'Taj naziv već postoji.'));
            if (process.env.TEAM_MANAGER_REPOSITORY_FIXTURE_OUT) {
                const fixture = require('node:fs').readFileSync(process.env.TEAM_MANAGER_REPOSITORY_FIXTURE_OUT, 'utf8');
                const demo = JSON.parse(fixture);
                const historyPath = '/RepositoryHistory?projectId=' + demo.ProjectId + '&repositoryId=' + demo.RepositoryId;
                await admin.goto(historyPath);
                const diffPath = await admin.locator('tbody a').first().getAttribute('href');
                assert.ok(diffPath.includes('commit='));
                await admin.goto(diffPath);
                assert.ok((await admin.locator('pre').textContent()).includes('diff --git'));
            }
        }
        console.log('PASS: Oracle repository metadata, inherited visibility, separate history/manage rights, HR/EN validation and configured local commits/diff.');

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
        // Cross multiple Oracle fetch buffers and retain every byte, including zeros.
        const content = Buffer.concat([Buffer.from('Oracle binary attachment \u0000 \u010d \ud83d\ude80', 'utf8'), Buffer.from(Array.from({ length: 262144 }, (_, i) => i % 256))]);
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
        await checkStatus(reader, articlePath, 404);
        await checkStatus(reader, documentDownload, 404);
        await checkStatus(reader, wikiArticle + "&revision=1", 404);
        await checkStatus(reader, topicPath, 404);
        await checkStatus(reader, repositoriesPath, 404);
        console.log('PASS: membership revocation protects direct issue and attachment URLs immediately.');

        const guard = await post(admin, '/Roles', {
            name: 'Administrator', description: 'QA guard', create: 'false'
        });
        assert.ok((await guard.text()).includes('class="validation-summary-errors"'));
        await admin.goto('/Roles');
        assert.equal(new URL(admin.url()).pathname, '/Roles');
        console.log('PASS: attempted removal of the last administrator rolls back.');
        await post(admin, removeRepositoryPath, {});
        await post(admin, documentsPath + '&handler=Remove&documentId=' + documentId, {});
        await checkStatus(admin, documentDownload, 404);

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
        await checkStatus(admin, '/ProjectDocuments?projectId=' + parent, 200);
        await checkStatus(admin, '/Wiki?projectId=' + parent, 200);
        await checkStatus(admin, '/Forum?projectId=' + parent, 200);
        await checkStatus(admin, '/Repositories?projectId=' + parent, 200);
        await checkStatus(admin, '/MailIntake?projectId=' + parent, 503);
        console.log('READY: ' + baseURL + '/Projects');
    } finally {
        await browser.close();
    }
}

run().catch(error => { console.error(error); process.exitCode = 1; });
