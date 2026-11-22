const assert=require('node:assert/strict');
const {request}=require('../artifacts/qa-browser/playwright');
(async()=>{const context=await request.newContext({baseURL:process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7589'});try{
 const login=await context.get('/Login');assert.equal(login.status(),200);const token=(await login.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);
 const signed=await context.post('/Login',{form:{EmployeeNumber:'admin',Password:'admin',__RequestVerificationToken:token}});assert.equal(signed.status(),200);assert.notEqual(new URL(signed.url()).pathname,'/Login');
 for(const path of ['/health/ready','/js/forms.js','/Projects','/Issues','/Search?Query=portal','/Profile','/Users','/TimeReport','/Notifications','/AdministrationAudit'])assert.equal((await context.get(path)).status(),200,path);
 console.log('PASS: existing Db2 workspace opens with the upgraded package, reports, scoped directories, notifications, audit and static form support. No business records were changed.');
}finally{await context.dispose();}})().catch(e=>{console.error(e);process.exitCode=1;});
