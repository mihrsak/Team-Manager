const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7623',observe=process.argv.includes('--observe');
assert.ok(['localhost','127.0.0.1'].includes(new URL(baseURL).hostname));assert.ok(!['7610','7589'].includes(new URL(baseURL).port));
async function post(page,url,fields,expected=200){const r=await page.request.get(url);assert.equal(r.status(),200);const token=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];const result=await page.request.post(url,{form:{...fields,__RequestVerificationToken:token}});assert.equal(result.status(),expected);return result;}
async function axeCheck(page){await page.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});assert.deepEqual(await page.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id)),[]);}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const admin=await browser.newPage({baseURL});await post(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});await admin.goto('/Profile');const original=await admin.locator('[name=language]').inputValue();const stamp=Date.now(),needle='R11Filters'+stamp,employee='nf'+stamp,password='Notifications-QA-2026';
 try{
  const projectResult=await post(admin,'/Project',{'Input.Name':needle,'Input.State':'Active','Input.Revision':'0'});const project=new URL(projectResult.url()).searchParams.get('id');
  const issueResult=await post(admin,'/Issue?projectId='+project+'&handler=Save',{'Input.Title':needle,'Input.Revision':'0','Input.Type':'Zadatak','Input.Status':'Novo','Input.Priority':'Normalan'});const issue=new URL(issueResult.url()).searchParams.get('id');
  await post(admin,'/Users?handler=Create',{employee,name:needle+' reader',job:'QA',password});await admin.goto('/Users');const profile=await admin.getByRole('row').filter({hasText:employee}).getByRole('link').getAttribute('href');await post(admin,profile+'&handler=Membership',{kind:'project',scopeId:project,role:'Developer'});
  const context=await browser.newContext({baseURL}),viewer=await context.newPage();try{
   await post(viewer,'/Login',{EmployeeNumber:employee,Password:password});await post(viewer,'/Issue?id='+issue+'&handler=Watch',{watch:'true'});
   for(const language of observe?['hr']:['hr','en']){
    await post(admin,'/Profile?handler=Language',{language});await post(viewer,'/Profile?handler=Language',{language});
    await post(admin,'/Issue?id='+issue+'&handler=Comment',{text:'Filter test '+language+' '+stamp});
    for(const [page,route,empty] of [[viewer,'/Notifications',language==='hr'?'Nema obavijesti za praćene zadatke.':'No notifications for watched issues.'],[admin,'/AdministrationAudit',language==='hr'?'Nema zabilježenih administrativnih radnji.':'No administrative actions recorded.']]){
     const cases=['PageNumber=bad','PageNumber=1&PageNumber=2','Query='+encodeURIComponent('x'.repeat(201)),'Query='+needle+'&Query='+needle,'PageNumber=0','PageNumber='+(route==='/Notifications'?10001:5001)];
     for(const filter of cases){
      await page.goto(route+'?'+filter);const query=await page.locator('#Query').inputValue(),rows=await page.locator('tbody tr').count(),alerts=await page.locator('[role=alert]').count(),pagination=await page.locator('.pagination').count();
      console.log(JSON.stringify({language,route,filter:filter.startsWith('Query=')&&filter.length>90?'Query=201chars':filter,rows,alerts,pagination,queryLength:query.length}));
      if(!observe){assert.equal(rows,0);assert.equal(alerts,1);assert.equal(pagination,0);assert.ok(!(await page.locator('main').innerText()).includes(empty));assert.equal(await page.locator('[role=alert]').isVisible(),true);assert.equal(await page.locator('html').getAttribute('lang'),language);const message=await page.locator('[role=alert]').innerText();if(filter==='PageNumber=bad')assert.ok(message.includes(language==='hr'?"Vrijednost 'bad' nije valjana za polje Stranica.":"The value 'bad' is not valid for Page."));else if(filter.includes('&'))assert.ok(message.includes(language==='hr'?'Svaki filtar smije imati samo jednu vrijednost.':'Each filter must have only one value.'));else if(filter.startsWith('Query='))assert.ok(message.includes(language==='hr'?'Polje Pretraga može imati najviše 200 znakova.':'The Search field must contain at most 200 characters.'));else assert.ok(message.includes(language==='hr'?'Unesi valjan broj.':'Enter a valid number.'));if(filter.startsWith('Query=')&&filter.length>90)assert.equal(query,'x'.repeat(201));}
     }
     if(!observe){await axeCheck(page);await page.goto(route+'?Query='+needle);assert.ok(await page.locator('tbody tr').count()>0);assert.equal(await page.locator('[role=alert]').count(),0);await page.goto(route+'?Query=no-match-'+stamp);assert.equal(await page.locator('tbody tr').count(),0);assert.ok((await page.locator('main').innerText()).includes(empty));}
    }
    const url='/Notifications?Query='+needle;await viewer.goto(url);const form=viewer.locator('form[action*="handler=Read"]').first();assert.equal(await form.count(),1);const action=await form.getAttribute('action'),csrf=await form.locator('[name=__RequestVerificationToken]').inputValue();
    const badAction=new URL(action,baseURL);badAction.searchParams.set('PageNumber','bad');
    const invalid=await viewer.request.post(badAction.href,{form:{__RequestVerificationToken:csrf}});assert.equal(invalid.status(),200);await viewer.goto(url);const unreadAfter=await viewer.locator('form[action*="handler=Read"]').count();
    console.log(JSON.stringify({language,kind:'invalid-read-filter',unreadAfter}));
    if(!observe){assert.equal(unreadAfter,1);await Promise.all([viewer.waitForNavigation(),viewer.locator('form[action*="handler=Read"]').first().locator('button').click()]);assert.equal(await viewer.locator('form[action*="handler=Read"]').count(),0);assert.equal(new URL(viewer.url()).searchParams.get('Query'),needle);}
    if(!observe)console.log('PASS: '+language+' notification/audit malformed, duplicate, long and range filters; no results/mutation on error; valid filtering, mark-read and axe');
   }
   if(!observe){assert.equal((await viewer.request.get('/AdministrationAudit?PageNumber=bad')).status(),403);await post(admin,profile+'&handler=Remove',{kind:'project',scopeId:project});await viewer.goto('/Notifications?Query='+needle);assert.equal(await viewer.locator('tbody tr').count(),0);}
  }finally{await context.close();}
 }finally{await post(admin,'/Profile?handler=Language',{language:original});}
}finally{await browser.close();}})().catch(e=>{console.error(JSON.stringify({error:e.name,message:String(e.message).split(/[\r\n]/)[0],actual:e.actual,expected:e.expected,at:e.stack?.split(/[\r\n]/).find(x=>x.includes('Test-NotificationAuditFilters.cjs:'))}));process.exitCode=1;});
