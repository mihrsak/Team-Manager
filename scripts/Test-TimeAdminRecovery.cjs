const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7443';
const observe=process.argv.includes('--observe');
assert.ok(['localhost','127.0.0.1'].includes(new URL(baseURL).hostname));
assert.ok(!['7610','7589'].includes(new URL(baseURL).port));
async function post(p,path,form){
 const page=await p.request.get(path);assert.equal(page.status(),200);
 const token=(await page.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];
 return p.request.post(path,{form:{...form,__RequestVerificationToken:token}});
}
async function save(p,path,form){const response=await post(p,path,form);assert.equal(response.status(),200);return response;}
async function submit(p,f){await Promise.all([p.waitForNavigation(),f.locator('button').first().click()]);}
const guard=p=>p.evaluate(()=>!window.dispatchEvent(new Event('beforeunload',{cancelable:true})));
async function axeCheck(p){await p.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});assert.deepEqual(await p.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id)),[]);}
(async()=>{
 const browser=await chromium.launch({channel:'msedge'});
 try{
  const p=await browser.newPage({baseURL});p.on('dialog',d=>d.accept());
  await save(p,'/Login',{EmployeeNumber:'admin',Password:'admin'});
  await p.goto('/Profile');const original=await p.locator('[name=language]').inputValue();
  try{for(const language of observe?['hr']:['hr','en']){
   await save(p,'/Profile?handler=Language',{language});const stamp=Date.now();
   const project=await save(p,'/Project',{'Input.Name':'Review9 '+stamp,'Input.State':'Active','Input.Revision':'0'});
   const projectId=new URL(project.url()).searchParams.get('id');
   const issue=await save(p,'/Issue?handler=Save&projectId='+projectId,{'Input.Title':'Review9 time '+stamp,'Input.Type':'Zadatak','Input.Status':'Novo','Input.Priority':'Normalan','Input.Revision':'0'});
   const issueId=new URL(issue.url()).searchParams.get('id'),issuePath='/Issue?id='+issueId;
   await save(p,issuePath+'&handler=Time',{date:'2026-11-26',hours:'1',activity:'QA',note:'Original '+stamp});
   await p.goto(issuePath);const path=await p.locator('a[href^="/TimeEntry?"]').first().getAttribute('href');await p.goto(path);
   const f=p.locator('form').filter({has:p.locator('#Token')}),token=await p.locator('#Token').inputValue();
   const ids={IssueId:issueId,Id:await p.locator('#Id').inputValue()};
   const concurrent=async token=>save(p,path,{...ids,Token:token,Date:'2026-11-26',Hours:'2',Activity:'Other saved activity',Note:'Other saved note '+stamp});
   await p.locator('#Hours').fill('3.25');await p.locator('#Activity').fill('Draft activity');const draft='\n\n Private time <safe> '+stamp;await p.locator('#Note').fill(draft);
   await concurrent(token);await submit(p,f);
   console.log(JSON.stringify({language,kind:'time-conflict',preserved:await p.locator('#Note').inputValue()===draft,guard:await guard(p),review:await f.locator('[name=resolveConflict]').count()}));
   if(!observe){
    assert.equal(await p.locator('#Token').inputValue(),token);assert.equal(await guard(p),true);assert.equal(await p.locator('#Note').inputValue(),draft);
    assert.equal(await f.locator('[name=resolveConflict]').count(),1);assert.ok((await p.locator('[data-current-time]').innerText()).includes('Other saved note '+stamp));
    assert.equal(await p.locator('safe').count(),0);await axeCheck(p);
    const current=await f.locator('[name=reviewedToken]').inputValue();
    await save(p,path,{...ids,Token:current,Date:'2026-11-26',Hours:'4',Activity:'Third saved activity',Note:'Third saved note'});
    await f.locator('[name=resolveConflict]').check();await submit(p,f);assert.equal(await p.locator('#Token').inputValue(),current);assert.equal(await p.locator('#Note').inputValue(),draft);assert.ok((await p.locator('[data-current-time]').innerText()).includes('Third saved note'));
    await f.locator('[name=resolveConflict]').check();await submit(p,f);await p.goto(path);assert.equal(await p.locator('#Hours').inputValue(),'3.25');assert.equal(await p.locator('#Note').inputValue(),draft);assert.equal(await guard(p),false);
    await p.locator('#Hours').fill('25');await p.locator('#Note').fill('Validation draft');await submit(p,f);assert.equal(await p.locator('#Note').inputValue(),'Validation draft');assert.equal(await guard(p),true);assert.equal(await f.locator('[name=resolveConflict]').count(),0);
    const malformed=await post(p,path,{...ids,Token:await p.locator('#Token').inputValue(),Date:'2026-11-26',Hours:'5',Activity:'QA',Note:'must not save',resolveConflict:'bad'});assert.equal(malformed.status(),400);
   }
   const role='Review9-'+stamp;
   await save(p,'/Roles',{name:role,description:'Original role',rights:'issues.view',create:'true'});
   const badRole=await post(p,'/Roles',{name:role,description:'Incorrect role overwrite',rights:'issues.edit',create:'bad'});
   await p.goto('/Roles');let roleForm=p.locator('form').filter({has:p.locator('[name=name][value="'+role+'"]')});
   console.log(JSON.stringify({language,kind:'invalid-role-action',status:badRole.status(),description:await roleForm.locator('[name=description]').inputValue()}));
   if(!observe){assert.equal(badRole.status(),400);assert.equal(await roleForm.locator('[name=description]').inputValue(),'Original role');assert.equal(await roleForm.locator('[name=rights][value="issues.view"]').isChecked(),true);assert.equal(await roleForm.locator('[name=rights][value="issues.edit"]').isChecked(),false);}
   const employee='r9'+stamp;await save(p,'/Users?handler=Create',{employee,name:'Review9 user '+stamp,job:'QA',password:'Review9-local-tests-2026'});
   await p.goto('/Users');const profile=await p.getByRole('row').filter({hasText:employee}).getByRole('link').getAttribute('href');
   const badGlobal=await post(p,profile+'&handler=GlobalRole',{role,remove:'bad'});await p.goto(profile);
   console.log(JSON.stringify({language,kind:'invalid-global-action',status:badGlobal.status(),assigned:(await p.locator('section').filter({has:p.locator('form[action*="handler=GlobalRole"]')}).locator('p').first().innerText()).includes(role)}));
   if(!observe){assert.equal(badGlobal.status(),400);assert.equal((await p.locator('section').filter({has:p.locator('form[action*="handler=GlobalRole"]')}).locator('p').first().innerText()).includes(role),false);}
   else await save(p,profile+'&handler=GlobalRole',{role,remove:'true'});
   if(!observe){await save(p,profile+'&handler=GlobalRole',{role,remove:'false'});await save(p,profile+'&handler=GlobalRole',{role,remove:'true'});}
   const status='R9-'+stamp;const badStatus=await post(p,'/Workflow?handler=Value',{kind:'status',name:status,closed:'bad'});
   const badTransition=await post(p,'/Workflow?handler=Transition',{role,type:'Zadatak',from:'Novo',to:'U tijeku',remove:'bad'});
   console.log(JSON.stringify({language,kind:'invalid-workflow-actions',valueStatus:badStatus.status(),transitionStatus:badTransition.status()}));
   if(!observe){assert.equal(badStatus.status(),400);assert.equal(badTransition.status(),400);await p.goto('/Workflow');assert.equal(await p.locator('form[action*="handler=Catalog"] [name=name][value="'+status+'"]').count(),0);assert.equal(await p.locator('form[action*="handler=Transition"] input[name=role][value="'+role+'"]').count(),0);await save(p,'/Workflow?handler=Value',{kind:'status',name:status,closed:'true'});await save(p,'/Workflow?handler=Transition',{role,type:'Zadatak',from:'Novo',to:status,remove:'false'});await save(p,'/Workflow?handler=Transition',{role,type:'Zadatak',from:'Novo',to:status,remove:'true'});}

   if(!observe){
    await save(p,profile+'&handler=Membership',{kind:'project',scopeId:projectId,role:'Voditelj'});
    const context=await browser.newContext({baseURL}),limited=await context.newPage();
    try{
     await save(limited,'/Login',{EmployeeNumber:employee,Password:'Review9-local-tests-2026'});
     await save(limited,'/Profile?handler=Language',{language});
     await limited.goto(path);assert.equal(await limited.locator('#Token').count(),1);
     const token=await limited.locator('#Token').inputValue();
     const csrf=await limited.locator('[name=__RequestVerificationToken]').first().inputValue();
     const denied=await limited.request.post('/Roles',{form:{__RequestVerificationToken:csrf,name:role,description:'Unauthorized edit',create:'false',rights:'issues.edit'}});assert.equal(denied.status(),403);assert.equal(new URL(denied.url()).pathname,'/Denied');assert.match(denied.headers()['cache-control'],/no-store/);assert.doesNotMatch(denied.headers()['cache-control'],/public/);assert.ok((await denied.text()).includes('lang="'+language+'"'));
     await save(p,profile+'&handler=Remove',{kind:'project',scopeId:projectId});
     const revoked=await limited.request.post(path,{form:{...ids,__RequestVerificationToken:csrf,Token:token,Date:'2026-11-26',Hours:'3',Activity:'Private revoked draft',Note:'Private revoked note'}});
     assert.equal(revoked.status(),404);assert.ok(!(await revoked.text()).includes('Private revoked note'));
    }finally{await context.close();}
   }
   console.log(observe?'OBSERVATION COMPLETE':'PASS: '+language+' time conflict recovery, validation, malformed admin actions, valid add/remove and axe');
  }}finally{await save(p,'/Profile?handler=Language',{language:original});}
 }finally{await browser.close();}
})().catch(e=>{console.error(JSON.stringify({error:e.name,message:String(e.message).split(/[\r\n]/)[0],actual:e.actual,expected:e.expected,at:e.stack?.split(/[\r\n]/).find(x=>x.includes('Test-TimeAdminRecovery.cjs:'))}));process.exitCode=1;});
