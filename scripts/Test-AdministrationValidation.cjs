const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7624';
const observe=process.argv.includes('--observe');
assert.ok(['localhost','127.0.0.1'].includes(new URL(baseURL).hostname));
assert.ok(!['7610','7589'].includes(new URL(baseURL).port),'Use isolated QA data or the dedicated IIS test database.');

async function post(page,path,form){
 const response=await page.request.get(path);assert.equal(response.status(),200,'Load form');
 const token=(await response.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);
 return page.request.post(path,{form:{...form,__RequestVerificationToken:token}});
}
async function save(page,path,form){const response=await post(page,path,form);assert.equal(response.status(),200,'Successful form submission');return response;}
async function unitId(page,name){
 await page.goto('/Users');
 const option=page.locator('select[name=unit]').getByRole('option',{name,exact:true});
 assert.equal(await option.count(),1);return option.getAttribute('value');
}
async function profileState(page,path){
 await page.goto(path);
 const form=page.locator('form[action*="handler=Save"]');
 return {name:await form.locator('[name=name]').inputValue(),job:await form.locator('[name=job]').inputValue(),unit:await form.locator('[name=unit]').inputValue()};
}
async function axeCheck(page){
 await page.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});
 assert.deepEqual(await page.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id)),[]);
}

(async()=>{
 const browser=await chromium.launch({channel:'msedge'});
 try{
  const page=await browser.newPage({baseURL});
  await save(page,'/Login',{EmployeeNumber:'admin',Password:'admin'});
  await page.goto('/Profile');const originalLanguage=await page.locator('[name=language]').inputValue();
  try{for(const language of ['hr','en']){
   await save(page,'/Profile?handler=Language',{language});
   const stamp=Date.now(),prefix='R10 administration '+language+' '+stamp;
   const names={unit:prefix+' unit',team:prefix+' team'};
   let recoveredUnit;
   for(const kind of ['unit','team']){
    await save(page,'/Users?handler=Scope',{kind,name:names[kind]});
    const duplicate=await post(page,'/Users?handler=Scope',{kind,name:names[kind]});
    console.log(JSON.stringify({language,kind:'duplicate-'+kind,status:duplicate.status()}));
    if(!observe){
     assert.equal(duplicate.status(),200,'Duplicate returns validation page');
     await page.setContent(await duplicate.text());
     const message=language==='en'?'That name already exists.':'Taj naziv već postoji.';
     assert.ok((await page.locator('[role=alert]').innerText()).includes(message));
     await axeCheck(page);
    }
    await page.goto('/Users');
    if(kind==='unit')assert.equal(await page.locator('select[name=unit]').getByRole('option',{name:names[kind],exact:true}).count(),1);
    else assert.equal(await page.locator('section').filter({has:page.locator('form[action*="handler=Scope"]')}).locator('p').filter({hasText:names[kind]}).count(),1);
    const correctedName=names[kind]+' recovered';
    await save(page,'/Users?handler=Scope',{kind,name:correctedName});
    if(kind==='unit')recoveredUnit=await unitId(page,correctedName);
   }
   const originalUnit=await unitId(page,names.unit);
   const employee='av'+stamp+language,originalName=prefix+' user',originalJob='QA original';
   await save(page,'/Users?handler=Create',{employee,name:originalName,job:originalJob,unit:originalUnit,password:'Administration-QA-2026'});
   await page.goto('/Users');
   const profile=await page.getByRole('row').filter({hasText:employee}).getByRole('link').getAttribute('href');assert.ok(profile);
   const before=await profileState(page,profile);
   assert.deepEqual(before,{name:originalName,job:originalJob,unit:originalUnit});
   const invalid=await post(page,profile+'&handler=Save',{name:prefix+' invalid name',job:'Invalid changed job',unit:'2147483647'});
   console.log(JSON.stringify({language,kind:'invalid-profile-unit',status:invalid.status()}));
   if(!observe){
    assert.equal(invalid.status(),200,'Missing unit returns validation page');
    await page.setContent(await invalid.text());
    const message=language==='en'?'The organizational unit is invalid.':'Organizacijska jedinica nije valjana.';
    assert.ok((await page.locator('[role=alert]').innerText()).includes(message));await axeCheck(page);
   }
   assert.deepEqual(await profileState(page,profile),before,'Rejected request must preserve stored name, job and unit');
   await save(page,profile+'&handler=Save',{name:prefix+' corrected user',job:'QA corrected',unit:recoveredUnit});
   assert.deepEqual(await profileState(page,profile),{name:prefix+' corrected user',job:'QA corrected',unit:recoveredUnit});
   console.log(observe?'OBSERVED: '+language+'; unchanged profile and valid correction verified':'PASS: '+language+' duplicate unit/team validation, no duplicate insert, invalid unit rollback, valid correction and axe');
  }}finally{await save(page,'/Profile?handler=Language',{language:originalLanguage});}
 }finally{await browser.close();}
})().catch(error=>{
 console.error(JSON.stringify({error:error.name,message:String(error.message).split(/[\r\n]/)[0],actual:error.actual,expected:error.expected,at:error.stack?.split(/[\r\n]/).find(line=>line.includes('Test-AdministrationValidation.cjs:'))}));
 process.exitCode=1;
});
