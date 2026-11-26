const assert=require('node:assert/strict'),path=require('node:path'),{spawn}=require('node:child_process');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL,observe=process.argv.includes('--observe')||process.env.TEAM_MANAGER_REVIEW_OBSERVE==='true';
assert.match(process.env.ConnectionStrings__TeamManager||'',/Database=TeamManager_AccountTest_[0-9a-f]{32}(?:;|$)/i);
async function post(p,url,form){const r=await p.request.get(url);assert.equal(r.status(),200);const token=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];return p.request.post(url,{form:{...form,__RequestVerificationToken:token}});}
async function save(p,url,form){const r=await post(p,url,form);assert.equal(r.status(),200);return r;}
async function profile(p,employee){await p.goto('/Users');return p.getByRole('row').filter({hasText:employee}).getByRole('link').getAttribute('href');}
async function race(actor,id,role,mode,url,form){
 const r=await actor.request.get(url);assert.equal(r.status(),200);const csrf=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];
 const child=spawn(path.join(process.env.windir,'System32/WindowsPowerShell/v1.0/powershell.exe'),['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(__dirname,'Invoke-RoleRevocationRace.ps1'),'-ActorId',String(id),'-ActorRole',role,'-Mode',mode],{windowsHide:true,stdio:['ignore','pipe','pipe']});
 let output='',stderr='';const done=new Promise((resolve,reject)=>{child.on('error',reject);child.on('exit',code=>resolve(code));});
 child.stderr.on('data',chunk=>stderr+=chunk.toString());
 await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('QA role lock startup timed out')),10000);child.stdout.on('data',chunk=>{output+=chunk.toString();if(output.includes('LOCKED')){clearTimeout(timer);resolve();}});child.on('exit',()=>{clearTimeout(timer);if(!output.includes('LOCKED'))reject(new Error('QA role lock failed to start'));});});
 const response=await actor.request.post(url,{form:{...form,__RequestVerificationToken:csrf},maxRedirects:0});assert.equal(await done,0,'QA race coordinator exit');assert.ok(output.includes('RELEASED_AFTER_CONFIRMED_WAIT'));
 return response;
}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const admin=await browser.newPage({baseURL});await save(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});await admin.goto('/Profile');const original=await admin.locator('[name=language]').inputValue();
 try{for(const language of observe?['hr']:['hr','en']){
  await save(admin,'/Profile?handler=Language',{language});const stamp=Date.now(),actorRole='R10actor'+stamp,marker='R10marker'+stamp,employee='race'+stamp;
  await save(admin,'/Roles',{name:actorRole,description:'QA actor',rights:'roles.manage',create:'true'});
  await save(admin,'/Roles',{name:marker,description:'QA marker',rights:'issues.view',create:'true'});
  await save(admin,'/Users?handler=Create',{employee,name:'QA role race '+stamp,job:'QA',password:'Role-race-test-2026'});
  const actorProfile=await profile(admin,employee),actorId=new URL(actorProfile,baseURL).searchParams.get('id');
  const context=await browser.newContext({baseURL}),actor=await context.newPage();
  try{for(const action of ['save','assign','deactivate']){
   await save(admin,actorProfile+'&handler=Account',{accountAction:'activate'});
   await save(admin,actorProfile+'&handler=GlobalRole',{role:actorRole,remove:'false'});
   await save(actor,'/Login',{EmployeeNumber:employee,Password:'Role-race-test-2026'});await save(actor,'/Profile?handler=Language',{language});
   const created='R10saved'+Date.now();
   const response=await race(actor,actorId,actorRole,action==='deactivate'?'deactivate':'revoke',action==='assign'?actorProfile+'&handler=GlobalRole':'/Roles',action==='assign'?{role:marker,remove:'false'}:{name:created,description:'must be blocked after revocation',rights:'issues.view',create:'true'});
   let changed;
   if(action==='assign'){await admin.goto(actorProfile);changed=(await admin.locator('section').filter({has:admin.locator('form[action*="handler=GlobalRole"]')}).locator('p').first().innerText()).includes(marker);if(changed)await save(admin,actorProfile+'&handler=GlobalRole',{role:marker,remove:'true'});}
   else{await admin.goto('/Roles');changed=await admin.locator('form input[name=name][value="'+created+'"]').count()>0;}
   console.log(JSON.stringify({language,action,status:response.status(),location:response.headers().location,changed,waitConfirmed:true}));
   if(!observe){assert.equal(response.status(),302);assert.equal(new URL(response.headers().location,baseURL).pathname,'/Denied');assert.equal(changed,false);}
  }}finally{await context.close();await save(admin,actorProfile+'&handler=Account',{accountAction:'activate'});}
 }}finally{await save(admin,'/Profile?handler=Language',{language:original});}
 console.log(observe?'OBSERVATION COMPLETE':'PASS: SQL role save/assignment and actor deactivation races reject post-lock writes in HR/EN.');
}finally{await browser.close();}})().catch(e=>{console.error(JSON.stringify({error:e.name,message:String(e.message).split(/[\r\n]/)[0],actual:e.actual,expected:e.expected,at:e.stack?.split(/[\r\n]/).find(x=>x.includes('Test-RoleRevocationRace.cjs:'))}));process.exitCode=1;});
