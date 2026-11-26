const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7443';
const observe=process.argv.includes('--observe');
assert.ok(['localhost','127.0.0.1'].includes(new URL(baseURL).hostname));
assert.ok(!['7610','7589'].includes(new URL(baseURL).port),'Use an isolated fixture or the dedicated IIS test database.');
async function post(p,path,form){const get=await p.request.get(path);assert.equal(get.status(),200);const token=(await get.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];const result=await p.request.post(path,{form:{...form,__RequestVerificationToken:token}});assert.equal(result.status(),200);return result;}
async function submit(p,form){await Promise.all([p.waitForNavigation(),form.locator('button').first().click()]);}
const protects=p=>p.evaluate(()=>!window.dispatchEvent(new Event('beforeunload',{cancelable:true})));
async function axeCheck(p){await p.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});assert.deepEqual(await p.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id)),[]);}
(async()=>{
 const browser=await chromium.launch({channel:'msedge'});
 try{
  const admin=await browser.newPage({baseURL});await post(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});
  const stamp=Date.now(),employee='r10draft'+stamp,password='Review10-drafts-local-2026';
  const created=await post(admin,'/Project',{'Input.Name':'Review10 repository/email drafts '+stamp,'Input.State':'Active','Input.Revision':'0'});
  const projectId=new URL(created.url()).searchParams.get('id');assert.ok(projectId);
  await post(admin,'/Users?handler=Create',{employee,name:'Review10 draft user '+stamp,job:'QA',password});
  await admin.goto('/Users');const profile=await admin.getByRole('row').filter({hasText:employee}).getByRole('link').getAttribute('href');
  await post(admin,profile+'&handler=Membership',{kind:'project',scopeId:projectId,role:'Voditelj'});
  const context=await browser.newContext({baseURL}),p=await context.newPage();let dismiss=false;const dialogs=[];p.on('dialog',d=>{dialogs.push(d.type());return dismiss?d.dismiss():d.accept();});
  try{
   await post(p,'/Login',{EmployeeNumber:employee,Password:password});
   for(const language of observe?['hr']:['hr','en']){
    await post(p,'/Profile?handler=Language',{language});
    const path='/Repositories?projectId='+projectId;
    await p.goto(path);const form=p.locator('form[action*="handler=Add"]');
    const rawName='  Repository draft <safe> '+language+' '+stamp+'  ',http='http://example.test/draft-'+language+'-'+stamp+'.git';
    await form.locator('[name=name]').fill(rawName);await form.locator('[name=url]').fill(http);await submit(p,form);
    console.log(JSON.stringify({language,kind:'repository-invalid-https',namePreserved:await form.locator('[name=name]').inputValue()===rawName,urlPreserved:await form.locator('[name=url]').inputValue()===http,guard:await protects(p)}));
    if(!observe){assert.equal(await form.locator('[name=name]').inputValue(),rawName);assert.equal(await form.locator('[name=url]').inputValue(),http);assert.equal(await form.getAttribute('data-unsaved-draft'),'true');assert.equal(await protects(p),true);assert.equal(await p.locator('safe').count(),0);await axeCheck(p);await form.locator('[name=url]').fill(http.replace('http:','https:'));await submit(p,form);assert.equal(await protects(p),false);assert.equal(await form.locator('[name=name]').inputValue(),'');}
    const duplicate='Duplicate repo '+language+' '+stamp;
    await post(p,path+'&handler=Add',{name:duplicate,url:'https://example.test/original-'+language+'-'+stamp+'.git'});
    await p.goto(path);await form.locator('[name=name]').fill('  '+duplicate+'  ');const duplicateUrl='https://example.test/duplicate-'+language+'-'+stamp+'.git';await form.locator('[name=url]').fill(duplicateUrl);await submit(p,form);
    console.log(JSON.stringify({language,kind:'repository-duplicate',namePreserved:await form.locator('[name=name]').inputValue()==='  '+duplicate+'  ',urlPreserved:await form.locator('[name=url]').inputValue()===duplicateUrl,guard:await protects(p)}));
    if(!observe){assert.equal(await form.locator('[name=name]').inputValue(),'  '+duplicate+'  ');assert.equal(await form.locator('[name=url]').inputValue(),duplicateUrl);assert.equal(await protects(p),true);await form.locator('[name=name]').fill('Recovered repo '+language+' '+stamp);await submit(p,form);assert.equal(await protects(p),false);}
    await p.goto('/EmailNotifications');const emailForm=p.locator('form').filter({has:p.locator('[name=address]')});
    await emailForm.locator('[name=address]').fill('');await emailForm.locator('[name=sendWatchedIssues]').check();await emailForm.locator('[name=sendDueReminders]').check();await submit(p,emailForm);
    console.log(JSON.stringify({language,kind:'email-missing-address',watched:await emailForm.locator('[name=sendWatchedIssues]').isChecked(),reminders:await emailForm.locator('[name=sendDueReminders]').isChecked(),guard:await protects(p)}));
    if(!observe){
     assert.equal(await emailForm.locator('[name=address]').inputValue(),'');assert.equal(await emailForm.locator('[name=sendWatchedIssues]').isChecked(),true);assert.equal(await emailForm.locator('[name=sendDueReminders]').isChecked(),true);assert.equal(await emailForm.getAttribute('data-unsaved-draft'),'true');assert.equal(await protects(p),true);
     const rawAddress='  invalid <safe> '+language+'  ';await emailForm.locator('[name=address]').evaluate(e=>e.type='text');await emailForm.locator('[name=address]').fill(rawAddress);await submit(p,emailForm);assert.equal(await emailForm.locator('[name=address]').inputValue(),rawAddress.trim());assert.equal(await emailForm.locator('[name=address]').getAttribute('value'),rawAddress);assert.equal(await emailForm.locator('[name=sendWatchedIssues]').isChecked(),true);assert.equal(await emailForm.locator('[name=sendDueReminders]').isChecked(),true);assert.equal(await p.locator('safe').count(),0);await axeCheck(p);
     const before=p.url(),count=dialogs.length;dismiss=true;await p.locator('main a[href="/Profile"]').click();assert.equal(p.url(),before);assert.equal(dialogs.length,count+1);assert.equal(dialogs.at(-1),'confirm');dismiss=false;
     const address='qa-review10-'+stamp+'@example.test';await emailForm.locator('[name=address]').fill(address);await emailForm.locator('[name=sendDueReminders]').uncheck();await submit(p,emailForm);assert.equal(await protects(p),false);await p.reload();assert.equal(await emailForm.locator('[name=address]').inputValue(),address);assert.equal(await emailForm.locator('[name=sendWatchedIssues]').isChecked(),true);assert.equal(await emailForm.locator('[name=sendDueReminders]').isChecked(),false);
    }
    await post(p,'/EmailNotifications',{address:'',sendWatchedIssues:'false',sendDueReminders:'false'});
    console.log(observe?'OBSERVATION COMPLETE':'PASS: '+language+' repository invalid/duplicate drafts, private email draft, guards, recovery and axe');
   }
   if(!observe){
    const path='/Repositories?projectId='+projectId;await p.goto(path);const form=p.locator('form[action*="handler=Add"]');await form.locator('[name=name]').fill('Private revoked draft '+stamp);await form.locator('[name=url]').fill('http://example.test/revoked.git');await submit(p,form);assert.equal(await protects(p),true);const csrf=await form.locator('[name=__RequestVerificationToken]').inputValue();await post(admin,profile+'&handler=Remove',{kind:'project',scopeId:projectId});const revoked=await p.request.post(path+'&handler=Add',{form:{__RequestVerificationToken:csrf,name:'Private revoked draft '+stamp,url:'https://example.test/revoked.git'}});assert.equal(revoked.status(),404);assert.ok(!(await revoked.text()).includes('Private revoked draft '+stamp));
   }
  }finally{await context.close();}
 }finally{await browser.close();}
})().catch(e=>{console.error(JSON.stringify({error:e.name,message:String(e.message).split(/[\r\n]/)[0],actual:e.actual,expected:e.expected,at:e.stack?.split(/[\r\n]/).find(x=>x.includes('Test-RepositoryEmailDrafts.cjs:'))}));process.exitCode=1;});
