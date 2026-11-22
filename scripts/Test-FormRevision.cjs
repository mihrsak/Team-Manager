const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7598';
async function post(page,path,fields){const get=await page.request.get(path);assert.equal(get.status(),200);const token=(await get.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);const response=await page.request.post(path,{form:{...fields,__RequestVerificationToken:token}});assert.equal(response.status(),200);return response;}
(async()=>{
 const browser=await chromium.launch({channel:'msedge'});
 try{
  const page=await browser.newPage({baseURL});await post(page,'/Login',{EmployeeNumber:'admin',Password:'admin'});
  const suffix=Date.now();const created=await post(page,'/Project',{'Input.Name':'Form revision '+suffix,'Input.State':'Active','Input.Revision':'0','Input.IsPublic':'false'});
  const project=new URL(created.url()).searchParams.get('id');assert.ok(Number(project)>0);
  const added=await post(page,'/Issue?projectId='+project+'&handler=Save',{'Input.Title':'Original '+suffix,'Input.Type':'Zadatak','Input.Status':'Novo','Input.Priority':'Normalan','Input.Revision':'0','Input.IsPrivate':'false'});
  const issue=new URL(added.url()).searchParams.get('id');assert.ok(Number(issue)>0);
  for(const language of ['en','hr']){
   await post(page,'/Profile?handler=Language',{language});await page.goto('/Issue?id='+issue);
   const title=page.getByRole('textbox',{name:language==='en'?'Title':'Naslov',exact:true});
   await title.fill('Unsaved '+language);
   page.once('dialog',async dialog=>{assert.equal(dialog.type(),'confirm');assert.match(dialog.message(),language==='en'?/unsaved changes/:/nespremljene izmjene/);await dialog.dismiss();});
   await page.getByRole('link',{name:language==='en'?'My profile':'Moj profil',exact:true}).click();
   assert.equal(new URL(page.url()).pathname,'/Issue');assert.equal(await title.inputValue(),'Unsaved '+language);
   page.once('dialog',dialog=>dialog.accept());await page.getByRole('link',{name:language==='en'?'My profile':'Moj profil',exact:true}).click();
   await page.waitForURL('**/Profile');await page.goto('/Issue?id='+issue);assert.equal(await title.inputValue(),'Original '+suffix);
   assert.ok(!(await page.locator('main').innerText()).includes(' · created'));
   await page.goto('/Profile');assert.equal(await page.getByRole('heading',{name:language==='en'?'Projects':'Projekti',exact:true}).count(),1);
   await page.goto('/Workflow');const form=page.locator('form').filter({has:page.getByRole('button',{name:language==='en'?'Add value':'Dodaj vrijednost',exact:true})});
   const kind=form.locator('[name=kind]');await kind.selectOption('priority');assert.equal(await form.locator('[name=name]').getAttribute('maxlength'),'20');
   await kind.selectOption('status');assert.equal(await form.locator('[name=name]').getAttribute('maxlength'),'30');
   await kind.selectOption('type');assert.equal(await form.locator('[name=name]').getAttribute('maxlength'),'50');
   // Submit overlength data through the HTTP test client to exercise server validation as well.
   await post(page,'/Workflow?handler=Value',{kind:'priority',name:'X'.repeat(22)});
   const invalid=await page.request.get('/Workflow');assert.equal(invalid.status(),200);
   // The actual POST error page must focus its accessible summary when rendered in a browser.
   await page.goto('/Workflow');
   const invalidForm=page.locator('form').filter({has:page.getByRole('button',{name:language==='en'?'Add value':'Dodaj vrijednost',exact:true})});
   await invalidForm.locator('[name=kind]').selectOption('priority');
   // Empty is also rejected by native validation, so use the existing duplicate priority.
   await invalidForm.locator('[name=name]').fill('Normalan');
   await invalidForm.getByRole('button').click();
   await page.locator('[data-validation-focus]').waitFor();
   assert.equal(await page.locator('[data-validation-focus]').getAttribute('role'),'alert');
   assert.equal(await page.evaluate(()=>document.activeElement?.id),'validation-errors');
   await page.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});
   const errors=await page.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id));assert.deepEqual(errors,[]);
  }
  await post(page,'/Profile?handler=Language',{language:'hr'});
  console.log('PASS: HR/EN dirty-form stay/discard, original data retention, translated profile/history, per-kind limits, accessible validation focus and error-state axe scans.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
