const assert=require('node:assert/strict'),path=require('node:path'),{spawn}=require('node:child_process');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL,observe=process.argv.includes('--observe')||process.env.TEAM_MANAGER_REVIEW_OBSERVE==='true';
assert.ok(['localhost','127.0.0.1'].includes(new URL(baseURL).hostname));
assert.match(process.env.ConnectionStrings__TeamManager||'',/Database=TeamManager_AccountTest_[0-9a-f]{32}(?:;|$)/i);
async function post(p,url,form,options={}){const r=await p.request.get(url);assert.equal(r.status(),200,'Load QA form');const token=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];return p.request.post(url,{form:{...form,__RequestVerificationToken:token},...options});}
async function save(p,url,form){const r=await post(p,url,form);assert.equal(r.status(),200,'Save QA form');return r;}
async function profile(p,employee){await p.goto('/Users');return p.getByRole('row').filter({hasText:employee}).getByRole('link').getAttribute('href');}
async function rowCount(p,url,text){await p.goto(url);return p.locator('tbody tr').filter({hasText:text}).count();}
async function registrationId(p,employee){await p.goto('/RegistrationRequests');const row=p.getByRole('row').filter({hasText:employee});assert.equal(await row.count(),1);return new URL(await row.locator('form').getAttribute('action'),baseURL).searchParams.get('id');}
async function race(actor,actorId,role,registrationId,mode,approve){
 const url='/RegistrationRequests?handler=Decision&id='+registrationId,r=await actor.request.get(url);assert.equal(r.status(),200);const csrf=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];
 const child=spawn(path.join(process.env.windir,'System32/WindowsPowerShell/v1.0/powershell.exe'),['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(__dirname,'Invoke-RegistrationRevocationRace.ps1'),'-ActorId',String(actorId),'-ActorRole',role,'-RegistrationId',String(registrationId),'-Mode',mode],{windowsHide:true,stdio:['ignore','pipe','pipe']});
 let output='';child.stderr.on('data',()=>{});
 const done=new Promise((resolve,reject)=>{child.on('error',reject);child.on('exit',resolve);});
 await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('QA registration lock startup timed out')),10000);child.stdout.on('data',chunk=>{output+=chunk.toString();if(output.includes('LOCKED')){clearTimeout(timer);resolve();}});child.on('exit',()=>{clearTimeout(timer);if(!output.includes('LOCKED'))reject(new Error('QA registration lock failed to start'));});});
 const response=await actor.request.post(url,{form:{approve:String(approve),__RequestVerificationToken:csrf},maxRedirects:0});assert.equal(await done,0,'QA registration coordinator exit');assert.ok(output.includes('RELEASED_AFTER_CONFIRMED_WAIT'));
 return response;
}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const admin=await browser.newPage({baseURL});await save(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});await admin.goto('/Profile');const original=await admin.locator('[name=language]').inputValue();
 try{for(const language of observe?['hr']:['hr','en']){
  await save(admin,'/Profile?handler=Language',{language});const stamp=Date.now(),actorRole='R12RegAdmin'+stamp,employee='r12actor'+stamp;
  await save(admin,'/Roles',{name:actorRole,description:'Registration decision QA',rights:'*',create:'true'});
  await save(admin,'/Users?handler=Create',{employee,name:'R12 registration actor '+stamp,job:'QA',password:'Registration-race-test-2026'});
  const actorProfile=await profile(admin,employee),actorId=new URL(actorProfile,baseURL).searchParams.get('id');
  const actorContext=await browser.newContext({baseURL}),actor=await actorContext.newPage(),anonymousContext=await browser.newContext({baseURL}),anonymous=await anonymousContext.newPage();
  let sequence=0;
  try{for(const mode of ['revoke','deactivate'])for(const approve of [true,false]){
   await save(admin,actorProfile+'&handler=Account',{accountAction:'activate'});await save(admin,actorProfile+'&handler=GlobalRole',{role:actorRole,remove:'false'});
   await save(actor,'/Login',{EmployeeNumber:employee,Password:'Registration-race-test-2026'});await save(actor,'/Profile?handler=Language',{language});
   const target='r12request'+stamp+(++sequence),display='R12 pending '+language+' '+mode+' '+approve+' '+stamp;
   await save(anonymous,'/Register',{employee:target,name:display,password:'Registration-request-test-2026',confirm:'Registration-request-test-2026'});
   const pendingId=await registrationId(admin,target);const response=await race(actor,actorId,actorRole,pendingId,mode,approve);
   const pending=await rowCount(admin,'/RegistrationRequests',target),accounts=await rowCount(admin,'/Users',target),audit=await rowCount(admin,'/AdministrationAudit?Query='+target,target);
   console.log(JSON.stringify({language,mode,approve,status:response.status(),location:response.headers().location,pending,accounts,audit,waitConfirmed:true}));
   if(!observe){assert.equal(response.status(),302);assert.equal(new URL(response.headers().location,baseURL).pathname,'/Denied');assert.equal(pending,1);assert.equal(accounts,0);assert.equal(audit,0);
    await save(admin,actorProfile+'&handler=Account',{accountAction:'activate'});await save(admin,actorProfile+'&handler=GlobalRole',{role:actorRole,remove:'false'});
    await save(actor,'/Login',{EmployeeNumber:employee,Password:'Registration-race-test-2026'});
    await save(actor,'/RegistrationRequests?handler=Decision&id='+pendingId,{approve:String(approve)});
    assert.equal(await rowCount(admin,'/RegistrationRequests',target),0);assert.equal(await rowCount(admin,'/Users',target),approve?1:0);assert.equal(await rowCount(admin,'/AdministrationAudit?Query='+target,target),1);
    const again=await post(admin,'/RegistrationRequests?handler=Decision&id='+pendingId,{approve:String(approve)});assert.equal(again.status(),404);assert.equal(await rowCount(admin,'/AdministrationAudit?Query='+target,target),1);
   }
  }}finally{await actorContext.close();await anonymousContext.close();await save(admin,actorProfile+'&handler=Account',{accountAction:'activate'});}
 }}finally{await save(admin,'/Profile?handler=Language',{language:original});}
 console.log(observe?'OBSERVATION COMPLETE':'PASS: SQL registration approve/reject revocation and deactivation races preserve pending/account/audit in HR/EN; valid retry and repeat decision verified.');
}finally{await browser.close();}})().catch(e=>{console.error(JSON.stringify({error:e.name,message:String(e.message).split(/[\r\n]/)[0],actual:e.actual,expected:e.expected,at:e.stack?.split(/[\r\n]/).find(x=>x.includes('Test-RegistrationRevocationRace.cjs:'))}));process.exitCode=1;});
