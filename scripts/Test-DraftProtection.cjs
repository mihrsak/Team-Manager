const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7618';
assert.ok(['localhost','127.0.0.1'].includes(new URL(baseURL).hostname),'Run only on an isolated local test instance.');
async function post(p,url,fields){const r=await p.request.get(url);assert.equal(r.status(),200,url);const token=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);const saved=await p.request.post(url,{form:{...fields,__RequestVerificationToken:token}});assert.equal(saved.status(),200,url);return saved;}
const protects=p=>p.evaluate(()=>!window.dispatchEvent(new Event('beforeunload',{cancelable:true})));
async function submit(p,form){await Promise.all([p.waitForNavigation(),form.locator('button').first().click()]);}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const p=await browser.newPage({baseURL});let dismiss=false;const dialogs=[];p.on('dialog',async d=>{dialogs.push({type:d.type(),message:d.message()});await(dismiss?d.dismiss():d.accept());});
 await post(p,'/Login',{EmployeeNumber:'admin',Password:'admin'});const suffix=Date.now();
 const projectResult=await post(p,'/Project',{'Input.Name':'Draft protection '+suffix,'Input.Revision':'0','Input.State':'Active','Input.IsPublic':'false'});const project=new URL(projectResult.url()).searchParams.get('id');assert.ok(project);
 const issueResult=await post(p,'/Issue?projectId='+project+'&handler=Save',{'Input.Title':'Draft fixture '+suffix,'Input.Type':'Zadatak','Input.Priority':'Normalan','Input.Status':'Novo','Input.Revision':'0'});const issue=new URL(issueResult.url()).searchParams.get('id');assert.ok(issue);
 const docs='/ProjectDocuments?projectId='+project;
 const token=await(await p.request.get(docs)).text();const upload=await p.request.post(docs+'&handler=Add',{multipart:{title:'Download fixture',description:'Draft protection',__RequestVerificationToken:token.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1],attachment:{name:'draft-proof.txt',mimeType:'text/plain',buffer:Buffer.from('Draft protection fixture')}}});assert.equal(upload.status(),200);
 const issuePath='/Issue?id='+issue;const attachmentToken=(await(await p.request.get(issuePath)).text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];assert.equal((await p.request.post(issuePath+'&handler=Upload',{multipart:{__RequestVerificationToken:attachmentToken,attachment:{name:'issue-proof.txt',mimeType:'text/plain',buffer:Buffer.from('Issue download fixture')}}})).status(),200);
 for(const language of ['hr','en']){
  await post(p,'/Profile?handler=Language',{language});
  const initial=await post(p,'/Wiki?projectId='+project+'&handler=Save',{revision:'0',title:'Wiki '+language+' '+suffix,body:'Initial version'});const wiki=new URL(initial.url()).searchParams.get('wikiId');assert.ok(wiki);const wikiPath='/Wiki?projectId='+project+'&wikiId='+wiki;
  await p.goto(wikiPath);const form=p.locator('form[action*="handler=Save"]');const draftTitle='  Draft '+language+' '+suffix+'  ',draftBody='  Draft body '+language+' čćž\nPreserve this text.  ';
  await form.locator('[name=title]').fill(draftTitle);await form.locator('[name=body]').fill(draftBody);
  await post(p,wikiPath+'&handler=Save',{revision:'1',title:'Current '+language+' '+suffix,body:'Current version 2'});
  await submit(p,form);
  assert.equal(await form.locator('[name=title]').inputValue(),draftTitle);assert.equal(await form.locator('[name=body]').inputValue(),draftBody);assert.equal(await form.locator('[name=revision]').inputValue(),'1');assert.equal(await form.locator('[name=reviewedRevision]').inputValue(),'2');assert.ok(await protects(p));
  assert.equal(await p.getByRole('heading',{name:language==='hr'?'Tvoj sačuvani nacrt':'Your preserved draft',exact:true}).count(),1);
  assert.equal(await form.locator('[name=resolveConflict]').getAttribute('required'),'');
  assert.equal(await p.evaluate(()=>document.activeElement.id),'validation-errors');assert.equal(await p.locator('[role=alert]').count(),1);
  const current=await p.request.get(wikiPath);assert.ok((await current.text()).includes('Current version 2'));
  // A stale retry without review must still not advance the saved version.
  await post(p,wikiPath+'&handler=Save',{revision:'1',title:draftTitle,body:draftBody});assert.ok((await(await p.request.get(wikiPath)).text()).includes('value="2"'));
  dismiss=true;const beforeStay=dialogs.length;await p.locator('nav a[href="/Projects"]').click();assert.equal(new URL(p.url()).pathname,'/Wiki');assert.equal(dialogs.length,beforeStay+1);assert.match(dialogs.at(-1).message,language==='hr'?/nespremljene/:/unsaved/);dismiss=false;
  // A second writer after review must produce another conflict, not overwrite.
  await form.locator('[name=resolveConflict]').check();await post(p,wikiPath+'&handler=Save',{revision:'2',title:'Current '+language+' '+suffix,body:'Current version 3'});await submit(p,form);
  assert.equal(await form.locator('[name=body]').inputValue(),draftBody);assert.equal(await form.locator('[name=revision]').inputValue(),'2');assert.equal(await form.locator('[name=reviewedRevision]').inputValue(),'3');assert.equal(await form.locator('[name=resolveConflict]').isChecked(),false);
  await p.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});assert.deepEqual(await p.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id)),[]);
  const beforeSave=dialogs.length;await form.locator('[name=resolveConflict]').check();await submit(p,form);assert.equal(dialogs.length,beforeSave);assert.equal(await form.locator('[name=revision]').inputValue(),'4');assert.equal(await form.locator('[name=body]').inputValue(),draftBody.trim());assert.equal(await protects(p),false);
  // Empty values must stay empty instead of falling back to the saved title.
  await form.locator('[name=title]').fill('');await form.locator('[name=title]').evaluate(e=>e.removeAttribute('required'));await submit(p,form);assert.equal(await form.locator('[name=title]').inputValue(),'');assert.equal(await form.locator('[name=body]').inputValue(),draftBody.trim());assert.ok(await protects(p));
  await p.goto(issuePath);const time=p.locator('form[action*="handler=Time"]');await time.locator('[name=date]').fill('1752-11-23');await time.locator('[name=hours]').fill('1,25');await time.locator('[name=activity]').fill('  Activity čćž  ');await time.locator('[name=note]').fill('  Keep note '+language+'  ');await submit(p,time);
  assert.equal(await time.locator('[name=date]').inputValue(),'1752-11-23');assert.equal(await time.locator('[name=hours]').inputValue(),'1,25');assert.equal(await time.locator('[name=activity]').inputValue(),'  Activity čćž  ');assert.equal(await time.locator('[name=note]').inputValue(),'  Keep note '+language+'  ');assert.ok(await protects(p));
  await time.locator('[name=date]').fill('2026-11-23');await time.locator('[name=hours]').fill('not a number');await submit(p,time);assert.equal(await time.locator('[name=hours]').inputValue(),'not a number');assert.equal(await time.locator('[name=note]').inputValue(),'  Keep note '+language+'  ');
  const beforeTime=dialogs.length;await time.locator('[name=hours]').fill('1,25');await submit(p,time);assert.equal(dialogs.length,beforeTime);assert.equal(await time.locator('[name=hours]').inputValue(),'');assert.equal(await protects(p),false);
  await p.goto(docs);await p.locator('[name=title]').fill('Before download');const beforeDownload=dialogs.length;assert.ok(await protects(p));await Promise.all([p.waitForEvent('download'),p.locator('a[download]').click()]);assert.equal(dialogs.length,beforeDownload);assert.ok(await protects(p));assert.equal(await p.locator('[name=title]').inputValue(),'Before download');await p.locator('[name=title]').fill('After download');assert.ok(await protects(p));
  // Exercise a real native beforeunload event: cancelling reload retains new text.
  dismiss=true;await p.reload({timeout:2000}).catch(e=>{assert.match(e.message,/ERR_ABORTED|interrupted|Timeout/);});assert.equal(await p.locator('[name=title]').inputValue(),'After download');assert.equal(dialogs.at(-1).type,'beforeunload');dismiss=false;
  const beforeLeave=dialogs.length;await p.locator('nav a[href="/Projects"]').click();await p.waitForURL('**/Projects');assert.equal(dialogs.length,beforeLeave+1);
  await p.goto(issuePath);await p.locator('#Input_Title').fill('Before attachment '+language);const beforeAttachment=dialogs.length;await Promise.all([p.waitForEvent('download'),p.locator('a[download]').click()]);assert.equal(dialogs.length,beforeAttachment);assert.ok(await protects(p));await p.locator('#Input_Title').fill('After attachment '+language);assert.ok(await protects(p));await submit(p,p.locator('form[action*="handler=Save"]'));assert.equal(await protects(p),false);
  await p.goto('/Issues?ProjectId='+project);await p.locator('input[name=selection]').first().check();assert.ok(await protects(p));const beforeCsv=dialogs.length;await Promise.all([p.waitForEvent('download'),p.locator('button[data-download]').click()]);assert.equal(dialogs.length,beforeCsv);assert.ok(await protects(p));assert.equal(await p.locator('input[name=selection]').first().isChecked(),true);
 }
 await post(p,'/Profile?handler=Language',{language:'hr'});
 console.log('PASS: HR/EN wiki drafts and explicit conflict review, repeat races, empty/invalid draft values, time draft recovery and valid save, document/attachment/CSV downloads, native reload cancellation and single navigation prompts.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
