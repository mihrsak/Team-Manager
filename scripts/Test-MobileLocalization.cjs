const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7620';
assert.ok(['localhost','127.0.0.1'].includes(new URL(baseURL).hostname),'Use an isolated local fixture.');
async function post(p,url,fields){const r=await p.request.get(url);assert.equal(r.status(),200,url);const token=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(token);const response=await p.request.post(url,{form:{...fields,__RequestVerificationToken:token}});assert.equal(response.status(),200,url);return response;}
async function errorText(p,response){return p.evaluate(html=>new DOMParser().parseFromString(html,'text/html').querySelector('[role=alert]')?.textContent.trim(),await response.text());}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const p=await browser.newPage({baseURL,viewport:{width:1440,height:1000}});await post(p,'/Login',{EmployeeNumber:'admin',Password:'admin'});const suffix=Date.now();
 const projectResponse=await post(p,'/Project',{'Input.Name':'Mobile locale '+suffix,'Input.State':'Active','Input.Revision':'0','Input.IsPublic':'false'});const project=new URL(projectResponse.url()).searchParams.get('id');assert.ok(project);
 const ids=[];
 for(const [name,start,due]of [['range','2027-04-05','2027-04-09'],['start','2027-04-05',''],['due','','2027-04-09']]){const r=await post(p,'/Issue?projectId='+project+'&handler=Save',{'Input.Title':name+' '+suffix,'Input.Type':'Zadatak','Input.Status':'Novo','Input.Priority':'Normalan','Input.Revision':'0','Input.StartDate':start,'Input.DueDate':due});const id=new URL(r.url()).searchParams.get('id');assert.ok(id);ids.push(id);}
 const issue='/Issue?id='+ids[0];
 await post(p,issue+'&handler=Time',{date:'2027-04-05',hours:'1.50',activity:'Calendar anchor start',note:'N'.repeat(1000)});
 await post(p,issue+'&handler=Time',{date:'2027-04-09',hours:'2.00',activity:'Calendar anchor end',note:'Long mobile content čćž'});
 await p.goto('/Profile');const originalTheme=await p.locator('select[name=theme]').inputValue();const originalLanguage=await p.locator('html').getAttribute('lang');const evidence=[];
 try{for(const language of ['hr','en']){
  await post(p,'/Profile?handler=Language',{language});await p.goto(issue);
  const startDisplay=(await p.locator('.issue-time-table tbody tr').filter({hasText:'Calendar anchor start'}).locator('td').first().innerText()).trim();const endDisplay=(await p.locator('.issue-time-table tbody tr').filter({hasText:'Calendar anchor end'}).locator('td').first().innerText()).trim();
  if(process.env.TEAM_MANAGER_EXPECT_DATE_START)assert.equal(startDisplay,process.env.TEAM_MANAGER_EXPECT_DATE_START);
  if(process.env.TEAM_MANAGER_EXPECT_DATE_END)assert.equal(endDisplay,process.env.TEAM_MANAGER_EXPECT_DATE_END);
  const detail=await p.locator('a[href*="/TimeEntry?"]').first().getAttribute('href');
  await p.goto('/Gantt?ProjectId='+project+'&Month=2027-04');
  const expected=[startDisplay+' – '+endDisplay,(language==='hr'?'Početak':'Start date')+' '+startDisplay,(language==='hr'?'Rok':'Due date')+' '+endDisplay];
  for(let i=0;i<ids.length;i++){const row=p.locator('#task-'+ids[i]);assert.equal(await row.locator('.gantt-period span').getAttribute('title'),expected[i]);assert.ok((await row.locator('th small').innerText()).endsWith(expected[i]));}
  evidence.push({language,startDisplay,endDisplay,gantt:expected});
  for(const theme of ['light','dark']){await post(p,'/Profile?handler=Theme',{theme});for(const width of [320,390]){
   await p.setViewportSize({width,height:850});await p.goto(issue);const measured=await p.evaluate(()=>{const region=document.querySelector('.table-scroll');return{document:document.documentElement.scrollWidth,viewport:innerWidth,region:region.clientWidth,table:region.scrollWidth};});assert.ok(measured.document<=width+1,JSON.stringify(measured));assert.ok(measured.table>measured.region,'The table must scroll locally');
   const region=p.getByRole('region',{name:language==='hr'?'Utrošeno vrijeme':'Time spent',exact:true});await region.focus();await p.keyboard.press('ArrowRight');await p.waitForFunction(()=>document.querySelector('.table-scroll').scrollLeft>0);await region.evaluate(e=>e.scrollLeft=e.scrollWidth);const last=region.locator('tbody tr').first().locator('td').last().locator('a');const box=await last.boundingBox(),container=await region.boundingBox();assert.ok(box.x>=container.x-1&&box.x+box.width<=container.x+container.width+1,'Last column is reachable');
   if(process.env.TEAM_MANAGER_REVIEW_OUTPUT){fs.mkdirSync(process.env.TEAM_MANAGER_REVIEW_OUTPUT,{recursive:true});await p.evaluate(()=>window.scrollTo(0,0));await p.screenshot({path:path.join(process.env.TEAM_MANAGER_REVIEW_OUTPUT,language+'-'+theme+'-'+width+'.png'),fullPage:true});}
   await p.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});assert.deepEqual(await p.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id)),[]);
   evidence.push({language,theme,width,...measured});
  }}
  await p.setViewportSize({width:1440,height:1000});await p.goto(detail);const token=await p.locator('#Token').inputValue();const fields={Token:token,Date:'2027-04-09',Hours:'2.00',Activity:'Calendar anchor end',Note:'Keep my draft'};
  await p.locator('#Activity').fill('');await p.locator('#Activity').evaluate(e=>e.removeAttribute('required'));await Promise.all([p.waitForNavigation(),p.locator('form[method=post]').last().locator('button').click()]);
  assert.equal((await p.locator('[role=alert]').innerText()).trim(),language==='hr'?'Polje Aktivnost je obavezno.':'The Activity field is required.');assert.equal(await p.locator('#Activity').getAttribute('aria-invalid'),'true');assert.equal(await p.locator('#Activity').getAttribute('aria-describedby'),'validation-errors');assert.equal(await p.evaluate(()=>document.activeElement.id),'validation-errors');
  const cases=[['Activity','A'.repeat(61),language==='hr'?'Polje Aktivnost može imati najviše 60 znakova.':'The Activity field must contain at most 60 characters.'],['Note','N'.repeat(1001),language==='hr'?'Polje Napomena može imati najviše 1000 znakova.':'The Note field must contain at most 1000 characters.'],['Date','not-a-date',language==='hr'?"Vrijednost 'not-a-date' nije valjana za polje Datum.":"The value 'not-a-date' is not valid for Date."]];
  for(const [field,value,message]of cases){const r=await post(p,detail,{...fields,[field]:value});assert.equal(await errorText(p,r),message);}
  const requiredProject=await post(p,'/Project',{'Input.Name':'','Input.Revision':'0','Input.State':'Active'});assert.equal(await errorText(p,requiredProject),language==='hr'?'Polje Naziv je obavezno.':'The Name field is required.');
  const lengthIssue=await post(p,issue+'&handler=Save',{'Input.Title':'T'.repeat(201),'Input.Type':'Zadatak','Input.Status':'Novo','Input.Priority':'Normalan','Input.Revision':'1'});assert.equal(await errorText(p,lengthIssue),language==='hr'?'Polje Naslov može imati najviše 200 znakova.':'The Title field must contain at most 200 characters.');
  const invalidRevision=await post(p,'/Project?id='+project,{'Input.Name':'Mobile locale '+suffix,'Input.State':'Active','Input.Revision':'not-a-number'});assert.equal(await errorText(p,invalidRevision),language==='hr'?"Vrijednost 'not-a-number' nije valjana za polje Verzija.":"The value 'not-a-number' is not valid for Version.");
  const login=await post(p,'/Login',{EmployeeNumber:'admin',Password:''});assert.equal(await errorText(p,login),language==='hr'?'Polje Lozinka je obavezno.':'The Password field is required.');
  await p.goto(detail);assert.equal(await p.locator('#Hours').inputValue(),'2.00');assert.equal(await p.locator('#Activity').inputValue(),'Calendar anchor end');
 }}finally{await post(p,'/Profile?handler=Theme',{theme:originalTheme});await post(p,'/Profile?handler=Language',{language:originalLanguage});}
 if(process.env.TEAM_MANAGER_REVIEW_OUTPUT)fs.writeFileSync(path.join(process.env.TEAM_MANAGER_REVIEW_OUTPUT,'measurements.json'),JSON.stringify(evidence,null,2));
 console.log('PASS: HR/EN 320/390px light/dark issue reflow, keyboard table scrolling and reachable columns, localized required/length/date-binding errors and field names without writes, plus Gantt range/start/due dates matching regional reports.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
