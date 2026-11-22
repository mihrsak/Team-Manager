const fs=require('node:fs'),assert=require('node:assert/strict'),path=require('node:path');
const {chromium}=require('../artifacts/qa-browser/playwright');
const directory=process.argv[2];assert.ok(directory,'Supply the ignored demo directory.');
const manifest=JSON.parse(fs.readFileSync(path.join(directory,'manifest.json'),'utf8'));
const access=JSON.parse(fs.readFileSync(path.join(directory,'LocalDemoAccess.private.json'),'utf8'));
const baseURL=access.Url;
async function post(page,target,fields){const response=await page.request.get(target);assert.equal(response.status(),200);const token=(await response.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);const r=await page.request.post(target,{form:{...fields,__RequestVerificationToken:token}});assert.equal(r.status(),200);return r;}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const admin=await browser.newPage({baseURL});await post(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});const project=manifest.projects[0].id;assert.equal((await admin.request.get('/js/forms.js')).status(),200);
 await admin.goto('/Projects');assert.equal(await admin.locator('a[href^="/Project?id="]').count(),3);
 await admin.goto('/Issues');assert.equal(await admin.locator('table').first().locator('tbody tr').count(),8);
 const pages=['/Projects','/Project?id='+project,'/Issues','/Issue?id='+manifest.issues[1],'/Roadmap?projectId='+project,'/Wiki?projectId='+project+'&wikiId='+manifest.wikiId,'/ProjectDocuments?projectId='+project,'/ProjectNews?projectId='+project,'/Forum?projectId='+project+'&topicId='+manifest.topicId,'/TimeReport?ProjectId='+project,'/Notifications','/AdministrationAudit','/Workflow'];
 await admin.goto('/Issue?id='+manifest.issues[0]);pages.push(await admin.locator('a[href*="/TimeEntry?"]').first().getAttribute('href'));
 for(const language of ['hr','en']){await post(admin,'/Profile?handler=Language',{language});for(const target of pages){const r=await admin.goto(target);assert.equal(r.status(),200,target);assert.equal(await admin.locator('html').getAttribute('lang'),language);assert.equal(await admin.locator('h1').count(),1,target);await admin.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});const violations=await admin.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id));assert.deepEqual(violations,[],target+' '+language);}}
 await admin.goto('/TimeReport?ProjectId='+project);assert.equal(await admin.locator('a[href*="/TimeEntry?"]').count(),3);await admin.locator('a[href*="/TimeEntry?"]').first().click();assert.equal(await admin.locator('#Hours').count(),1);
 const viewer=await browser.newPage({baseURL});await post(viewer,'/Login',{EmployeeNumber:'demo.viewer',Password:access.Accounts.Demo.Password});await viewer.goto('/Projects');assert.equal(await viewer.locator('a[href^="/Project?id="]').count(),2);assert.equal((await viewer.request.get('/Project?id='+manifest.projects[1].id)).status(),404);await viewer.goto('/Users');const lead=await viewer.getByRole('row').filter({hasText:'demo.lead'}).getByRole('link').getAttribute('href');const profile=await viewer.request.get(lead);assert.ok(!(await profile.text()).includes(manifest.projects[1].name));
 const dev=await browser.newPage({baseURL});await post(dev,'/Login',{EmployeeNumber:'demo.dev',Password:access.Accounts.Demo.Password});await dev.goto('/Projects');assert.equal(await dev.locator('a[href^="/Project?id="]').count(),3);await dev.goto('/Notifications');assert.ok(await dev.locator('tbody tr').count()>=1);
 await post(admin,'/Profile?handler=Language',{language:'hr'});
 console.log('PASS: clean three-project/eight-issue demo, complete collaboration content, HR/EN 28-page axe checks, time details, scoped project/profile privacy and watched notification.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
