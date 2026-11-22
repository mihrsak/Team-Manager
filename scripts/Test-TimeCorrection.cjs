const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7609';
async function post(page,path,fields){const get=await page.request.get(path);assert.equal(get.status(),200,path);const token=(await get.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);const r=await page.request.post(path,{form:{...fields,__RequestVerificationToken:token}});assert.equal(r.status(),200,path);return r;}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const admin=await browser.newPage({baseURL});await post(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});const suffix=Date.now(),title='TimeCorrection'+suffix;
 const p=await post(admin,'/Project',{'Input.Name':title,'Input.Revision':'0','Input.State':'Active','Input.IsPublic':'false'});const project=new URL(p.url()).searchParams.get('id');
 const created=await post(admin,'/Issue?projectId='+project+'&handler=Save',{'Input.Title':title,'Input.Type':'Zadatak','Input.Status':'Novo','Input.Priority':'Normalan','Input.Revision':'0'});const issue=new URL(created.url()).searchParams.get('id');assert.ok(issue);
 const pages=[];const profiles=[];
 for(let i=0;i<2;i++){const employee='time'+suffix+i,password='Time-QA-password-2026';await post(admin,'/Users?handler=Create',{employee,name:employee,password,job:title});await admin.goto('/Users');const profile=await admin.getByRole('row').filter({hasText:employee}).getByRole('link').getAttribute('href');profiles.push(profile);await post(admin,profile+'&handler=Membership',{kind:'project',scopeId:project,role:'Developer'});const page=await browser.newPage({baseURL});await post(page,'/Login',{EmployeeNumber:employee,Password:password});pages.push(page);}
 const [owner,other]=pages;
 await post(owner,'/Issue?id='+issue+'&handler=Time',{date:'2026-11-22',hours:'1.75',activity:title,note:'Original'});await owner.goto('/Issue?id='+issue);const path=await owner.locator('a[href*="/TimeEntry?"]').first().getAttribute('href');assert.ok(path);
 await owner.goto(path);const oldToken=await owner.locator('#Token').inputValue();
 const denied=await post(other,path,{Token:oldToken,Date:'2026-11-22',Hours:'8',Activity:title,Note:'Forbidden'});assert.ok(new URL(denied.url()).pathname.includes('Denied'));
 for(const language of ['hr','en']){
  await post(owner,'/Profile?handler=Language',{language});await owner.goto(path);const token=await owner.locator('#Token').inputValue();await post(owner,path,{Token:token,Date:'2026-11-21',Hours:'2.50',Activity:title,Note:'Corrected '+language});
  await owner.goto(path);assert.equal(await owner.locator('#Hours').inputValue(),'2.50');assert.equal(await owner.locator('html').getAttribute('lang'),language);
 }
 const stale=await post(owner,path,{Token:oldToken,Date:'2026-11-22',Hours:'9',Activity:title,Note:'Stale'});assert.ok((await stale.text()).includes('The time entry has changed. Reload the page.'));
 await admin.goto(path);const token=await admin.locator('#Token').inputValue();await post(admin,path,{Token:token,Date:'2026-11-21',Hours:'3.25',Activity:title,Note:'Manager correction'});
 await admin.goto('/TimeReport?ProjectId='+project+'&Activity='+title);assert.equal(await admin.locator('table').first().locator('tbody tr').count(),1);assert.match(await admin.locator('caption').first().innerText(),/3[,.]25/);assert.equal(await admin.locator('a[href*="/TimeEntry?"]').count(),1);
 await admin.goto('/Issue?id='+issue);const html=await admin.content();assert.ok(html.includes('Manager correction')&&html.includes('Original')&&html.includes('AuthorId'));
 await post(admin,profiles[0]+'&handler=Remove',{kind:'project',scopeId:project});assert.equal((await owner.request.get(path)).status(),404);
 console.log('PASS: HR/EN owner correction, other author denied, manager correction, stale edit rejected, history retained, report counted once and membership revocation enforced.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
