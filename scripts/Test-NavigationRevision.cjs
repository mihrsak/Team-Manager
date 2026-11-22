const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7598';
async function post(page,path,fields){const get=await page.request.get(path);assert.equal(get.status(),200);const token=(await get.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);const response=await page.request.post(path,{form:{...fields,__RequestVerificationToken:token}});assert.equal(response.status(),200);return response;}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const page=await browser.newPage({baseURL});await post(page,'/Login',{EmployeeNumber:'admin',Password:'admin'});
 const suffix=Date.now();const name='Navigation '+suffix;
 const added=await post(page,'/Project',{'Input.Name':name,'Input.Description':'Overview description','Input.Revision':'0','Input.State':'Active'});
 const project=new URL(added.url()).searchParams.get('id');assert.ok(Number(project)>0);
 let first;
 for(let i=0;i<52;i++){const response=await post(page,'/Issue?projectId='+project+'&handler=Save',{'Input.Title':name+' '+String(i).padStart(2,'0'),'Input.Type':'Zadatak','Input.Status':'Novo','Input.Priority':'Normalan','Input.Revision':'0','Input.DueDate':'2026-11-24'});const id=new URL(response.url()).searchParams.get('id');assert.ok(Number(id)>0);first??=id;}
 for(const language of ['en','hr']){
  await post(page,'/Profile?handler=Language',{language});
  const list='/Issues?ProjectId='+project+'&Query='+encodeURIComponent(name)+'&SortOrder=oldest';await page.goto(list);
  assert.equal(await page.locator('main .table tbody tr').count(),50);
  assert.equal(await page.getByRole('link',{name:language==='en'?'New issue':'Novi zadatak',exact:true}).count(),1);
  assert.equal(await page.locator('nav a[aria-current="page"]').innerText(),language==='en'?'Issues':'Zadaci');
  assert.match(await page.title(),language==='en'?/^Issues /:/^Zadaci /);
  const firstLink=page.locator('main .table tbody a').first();const path=await firstLink.getAttribute('href');const returnUrl=new URL(path,baseURL).searchParams.get('returnUrl');assert.ok(returnUrl.includes('ProjectId='+project)&&returnUrl.includes('SortOrder=oldest'));
  await firstLink.click();await page.waitForURL('**/Issue?**');
  const back=page.getByRole('link',{name:language==='en'?'← Project issues':'← Projektni zadaci',exact:true});assert.equal(await back.getAttribute('href'),returnUrl);
  const dateCells=await page.locator('main').innerText();assert.ok(!dateCells.includes('24.11.2026')||language==='hr');
  await back.click();await page.waitForURL('**/Issues?**');
  await page.getByRole('link',{name:language==='en'?'Next page':'Sljedeća stranica',exact:true}).click();await page.waitForURL('**PageNumber=2**');assert.equal(await page.locator('main .table tbody tr').count(),2);
  const csv=await page.request.get(list+'&handler=Csv');assert.equal(csv.status(),200);assert.equal((await csv.text()).trim().split('\r\n').length,53,'CSV must export all filtered rows');
  await page.goto('/Issue?id='+first+'&ReturnUrl='+encodeURIComponent('https://example.test/outside'));assert.ok((await page.getByRole('link',{name:language==='en'?'← Project issues':'← Projektni zadaci',exact:true}).getAttribute('href')).startsWith('/Issues?'));
  await page.goto('/Projects?Query='+encodeURIComponent(name));assert.equal(await page.locator('main tbody tr').count(),1);
  await page.goto('/Project?id='+project);assert.ok(await page.getByRole('heading',{name:language==='en'?'Project overview':'Pregled projekta',exact:true}).isVisible());assert.equal(await page.locator('details').getAttribute('open'),null);
  await page.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});const failures=await page.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id));assert.deepEqual(failures,[]);
 }
 await post(page,'/Profile?handler=Language',{language:'hr'});
 console.log('PASS: 52-row paging, complete CSV, safe filter-preserving return links, project search/overview, localized titles/navigation and HR/EN accessibility.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
