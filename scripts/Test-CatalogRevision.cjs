const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7613';
async function post(page,path,fields){const get=await page.request.get(path);assert.equal(get.status(),200,path);const token=(await get.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);const r=await page.request.post(path,{form:{...fields,__RequestVerificationToken:token}});assert.equal(r.status(),200,path);return r;}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const admin=await browser.newPage({baseURL});await post(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});const suffix=String(Date.now()).slice(-8),name='Catalog'+suffix,priority='Priority'+suffix,type='Type'+suffix,status='Status'+suffix;
 for(const [kind,value] of [['priority',priority],['type',type],['status',status]]){await post(admin,'/Workflow?handler=Value',{kind,name:value,closed:'false'});await post(admin,'/Workflow?handler=Catalog',{kind,name:value,order:'0',active:'true'});}
 const projectResponse=await post(admin,'/Project',{'Input.Name':name,'Input.Revision':'0','Input.State':'Active','Input.IsPublic':'false'});const project=new URL(projectResponse.url()).searchParams.get('id');
 const fields={'Input.Title':name,'Input.Type':type,'Input.Status':'Novo','Input.Priority':priority,'Input.Revision':'0'};
 await admin.goto('/Issue?projectId='+project);const options=await admin.locator('#Input_Priority option').allTextContents();assert.ok(options.indexOf(priority)<options.indexOf('Normalan'));
 const created=await post(admin,'/Issue?projectId='+project+'&handler=Save',fields);const issue=new URL(created.url()).searchParams.get('id');assert.ok(issue);
 await post(admin,'/Issue?id='+issue+'&handler=Save',{...fields,'Input.Revision':'1','Input.Status':status});
 for(const [kind,value] of [['priority',priority],['type',type],['status',status]])await post(admin,'/Workflow?handler=Catalog',{kind,name:value,order:'0',active:'false'});
 for(const language of ['hr','en']){
  await post(admin,'/Profile?handler=Language',{language});await admin.goto('/Issue?id='+issue);assert.equal(await admin.locator('#Input_Priority').inputValue(),priority);const revision=await admin.locator('#Input_Revision').inputValue();
  const saved=await post(admin,'/Issue?id='+issue+'&handler=Save',{...fields,'Input.Revision':revision,'Input.Status':status,'Input.Description':'Historical value preserved '+language});assert.ok(new URL(saved.url()).searchParams.get('id')===issue);
  await admin.goto('/Issue?projectId='+project);assert.equal(await admin.locator('#Input_Priority option').filter({hasText:priority}).count(),0);assert.equal(await admin.locator('#Input_Type option').filter({hasText:type}).count(),0);assert.equal(await admin.locator('#Input_Status option').filter({hasText:status}).count(),0);
  const refused=await post(admin,'/Issue?projectId='+project+'&handler=Save',fields);assert.ok(!(new URL(refused.url()).searchParams.get('id')),'Inactive new assignment was saved');assert.match(await refused.text(),language==='hr'?/Neaktivna vrijednost/:/An inactive value/);
 }
 const initial=await post(admin,'/Workflow?handler=Catalog',{kind:'status',name:'Novo',order:'0',active:'false'});assert.ok((await initial.text()).includes('The initial status Novo must remain active.'));
 await admin.goto('/AdministrationAudit?Query='+priority);assert.ok((await admin.content()).includes('workflow.value.update')||(await admin.content()).includes('Catalog value updated'));
 console.log('PASS: catalog rank, HR/EN activation controls, historical inactive values preserved, inactive new assignments rejected, initial status protected and changes audited.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
