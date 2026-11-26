const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),{spawnSync}=require('node:child_process');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL,settingsPath=process.env.TEAM_MANAGER_ADMIN_FIXTURE_SETTINGS;
const observe=process.argv.includes('--observe')||process.env.TEAM_MANAGER_REVIEW_OBSERVE==='true';
assert.ok(baseURL&&settingsPath,'Use an explicitly configured isolated administrator fixture.');
const url=new URL(baseURL);assert.equal(url.protocol,'https:');assert.equal(url.hostname,'localhost');assert.ok(['7633','7634'].includes(url.port));assert.equal(url.pathname,'/');
assert.equal(path.basename(settingsPath).toLowerCase(),'appsettings.local.json');
const ignored=spawnSync('git',['check-ignore','--quiet','--',settingsPath],{cwd:path.join(__dirname,'..'),windowsHide:true});assert.equal(ignored.status,0,'Administrator fixture settings must remain ignored.');
const settings=JSON.parse(fs.readFileSync(settingsPath,'utf8').replace(/^\uFEFF/,''));
assert.equal(settings.AdminFixture?.Isolated,true);assert.match(settings.AdminFixture?.Schema||'',/^TMR_ADMINQA_[A-F0-9]{12}$/);
async function post(p,url,form){const r=await p.request.get(url);assert.equal(r.status(),200,'Load administrator fixture form');const token=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];return p.request.post(url,{form:{...form,__RequestVerificationToken:token}});}
async function save(p,url,form){const r=await post(p,url,form);assert.equal(r.status(),200,'Save administrator fixture form');return r;}
async function profile(p,employee){await p.goto('/Users');const row=p.getByRole('row').filter({hasText:employee});assert.equal(await row.count(),1);return row.getByRole('link').getAttribute('href');}
function section(p,name){return p.locator('section.card').filter({has:p.getByRole('heading',{name,exact:true})});}
async function groupState(p,name,memberId,role){
 const r=await p.goto('/Groups');assert.equal(r.status(),200);const language=await p.locator('html').getAttribute('lang'),group=section(p,name);assert.equal(await group.count(),1);
 return {member:(await group.locator('select[name=userId] option[value="'+memberId+'"]').textContent()).includes(' · '+(language==='en'?'member':'član')),role:(await group.locator('select[name=role] option[value="'+role+'"]').textContent()).includes(' · '+(language==='en'?'assigned':'dodijeljena'))};
}
async function axeCheck(p,html){await p.setContent(html);await p.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});assert.deepEqual(await p.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id)),[]);}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const admin=await browser.newPage({baseURL});await save(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});await admin.goto('/Users');assert.equal(await admin.locator('tbody tr').count(),1,'Fixture must initially contain only admin.');
 const initialProfile=await profile(admin,'admin');assert.equal(new URL(initialProfile,baseURL).searchParams.get('id'),'1');
 const stamp=Date.now(),employee='r12group'+stamp,role='R12OnlyAdmin'+stamp,groupName='R12 last admin '+stamp;
 await save(admin,'/Roles',{name:role,description:'Isolated last group administrator QA',rights:'*',create:'true'});
 await save(admin,'/Users?handler=Create',{employee,name:'R12 group administrator '+stamp,job:'QA',password:'Group-admin-fixture-2026'});
 const actorProfile=await profile(admin,employee),actorId=new URL(actorProfile,baseURL).searchParams.get('id');assert.equal(await admin.locator('tbody tr').count(),2,'Only initial admin and the new QA user may exist.');
 await save(admin,'/Groups?handler=Create',{name:groupName});await admin.goto('/Groups');const group=section(admin,groupName);assert.equal(await group.count(),1);const groupId=new URL(await group.locator('form[action*="handler=Member"]').getAttribute('action'),baseURL).searchParams.get('groupId');assert.ok(groupId);
 await save(admin,'/Groups?handler=Role&groupId='+groupId,{role,remove:'false'});await save(admin,'/Groups?handler=Member&groupId='+groupId,{userId:actorId,remove:'false'});
 const context=await browser.newContext({baseURL}),actor=await context.newPage();let groupAdminReady=false;
 try{
  await save(actor,'/Login',{EmployeeNumber:employee,Password:'Group-admin-fixture-2026'});assert.equal((await actor.request.get('/Roles')).status(),200);groupAdminReady=true;
  await save(actor,initialProfile+'&handler=Account',{accountAction:'deactivate'});await actor.goto(initialProfile);assert.equal(await actor.locator('form[action*="handler=Account"] button[name=accountAction][value=activate]').count(),1);
  for(const language of ['hr','en']){
   await save(actor,'/Profile?handler=Language',{language});
   for(const action of ['Role','Member']){
    const result=await post(actor,'/Groups?handler='+action+'&groupId='+groupId,action==='Role'?{role,remove:'true'}:{userId:actorId,remove:'true'});
    const status=result.status(),html=await result.text();
    const state=await groupState(actor,groupName,actorId,role),effective=await actor.request.get('/Roles');
    console.log(JSON.stringify({language,action,status,memberRetained:state.member,roleRetained:state.role,effectiveFullAdmin:effective.status()===200}));
    assert.equal(state.member,true);assert.equal(state.role,true);assert.equal(effective.status(),200);
    if(observe)assert.ok([200,500].includes(status));
    else{assert.equal(status,200);assert.ok(html.includes(language==='en'?'At least one active full administrator is required.':'Mora ostati barem jedan aktivan puni administrator.'));await axeCheck(actor,html);}
   }
  }
 }finally{
  if(groupAdminReady){
   await save(actor,initialProfile+'&handler=Account',{accountAction:'activate'});
   await actor.goto(initialProfile);assert.equal(await actor.locator('form[action*="handler=Account"] button[name=accountAction][value=deactivate]').count(),1,'Initial administrator must be restored in finally.');
   console.log('RESTORED: initial administrator active.');
  }
 }
 try{
  await save(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});
  await save(admin,'/Groups?handler=Member&groupId='+groupId,{userId:actorId,remove:'true'});await save(admin,'/Groups?handler=Role&groupId='+groupId,{role,remove:'true'});
  const cleaned=await groupState(admin,groupName,actorId,role);assert.deepEqual(cleaned,{member:false,role:false});assert.equal((await actor.request.get('/Groups')).status(),403);
  console.log(observe?'OBSERVATION COMPLETE: rollback verified and initial administrator restored.':'PASS: HR/EN last group administrator role/member removal is validated with rollback and axe; initial administrator restored; valid removal and denied access verified.');
 }finally{await context.close();}
}finally{await browser.close();}})().catch(e=>{console.error(JSON.stringify({error:e.name,message:String(e.message).split(/[\r\n]/)[0],actual:e.actual,expected:e.expected,at:e.stack?.split(/[\r\n]/).find(x=>x.includes('Test-LastGroupAdministrator.cjs:'))}));process.exitCode=1;});
