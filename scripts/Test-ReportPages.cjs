const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7608';
async function post(page,path,fields){const get=await page.request.get(path);assert.equal(get.status(),200);const token=(await get.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);const response=await page.request.post(path,{form:{...fields,__RequestVerificationToken:token}});assert.equal(response.status(),200,path);return response;}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const admin=await browser.newPage({baseURL});await post(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});
 const suffix=Date.now(),needle='ReportPages'+suffix;
 const created=await post(admin,'/Project',{'Input.Name':needle,'Input.Revision':'0','Input.State':'Active','Input.IsPublic':'false'});const project=new URL(created.url()).searchParams.get('id');assert.ok(project);
 const issueResponse=await post(admin,'/Issue?projectId='+project+'&handler=Save',{'Input.Title':needle,'Input.Type':'Zadatak','Input.Status':'Novo','Input.Priority':'Normalan','Input.Revision':'0'});const issue=new URL(issueResponse.url()).searchParams.get('id');assert.ok(issue);
 const employee='reports'+suffix,password='Report-QA-password-2026';await post(admin,'/Users?handler=Create',{employee,name:employee,password,job:needle});await admin.goto('/Users');const profile=await admin.getByRole('row').filter({hasText:employee}).getByRole('link').getAttribute('href');await post(admin,profile+'&handler=Membership',{kind:'project',scopeId:project,role:'Developer'});
 const reader=await browser.newPage({baseURL});await post(reader,'/Login',{EmployeeNumber:employee,Password:password});await post(reader,'/Issue?id='+issue+'&handler=Watch',{watch:'true'});
 for(let i=0;i<51;i++)await post(admin,'/Issue?id='+issue+'&handler=Comment',{text:needle+' '+i});
 // Repeated membership changes produce 102 distinct, audited administrative actions.
 for(let i=0;i<51;i++){await post(admin,profile+'&handler=Remove',{kind:'project',scopeId:project});await post(admin,profile+'&handler=Membership',{kind:'project',scopeId:project,role:'Developer'});}
 for(const language of ['hr','en']){
  await post(admin,'/Profile?handler=Language',{language});await post(reader,'/Profile?handler=Language',{language});
  await reader.goto('/Notifications?Query='+needle);assert.equal(await reader.locator('tbody tr').count(),50);const first=await reader.locator('form[action*="handler=Read"]').first().getAttribute('action');await reader.goto('/Notifications?Query='+needle+'&PageNumber=2');assert.equal(await reader.locator('tbody tr').count(),1);assert.notEqual(await reader.locator('form[action*="handler=Read"]').first().getAttribute('action'),first);
  await admin.goto('/AdministrationAudit?Query=membership.');assert.equal(await admin.locator('tbody tr').count(),100);await admin.goto('/AdministrationAudit?Query=membership.&PageNumber=2');assert.ok(await admin.locator('tbody tr').count()>0);assert.equal(await admin.locator('html').getAttribute('lang'),language);
 }
 assert.ok(new URL((await reader.request.get('/AdministrationAudit')).url()).pathname.includes('Denied'));
 await post(admin,profile+'&handler=Remove',{kind:'project',scopeId:project});await reader.goto('/Notifications?Query='+needle);assert.equal(await reader.locator('tbody tr').count(),0);
 console.log('PASS: HR/EN older notifications/audit pages, filters, non-overlapping notifications, administrator-only audit and immediate visibility revocation.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
