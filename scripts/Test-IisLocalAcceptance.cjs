const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL='https://localhost:7443';
async function post(p,path,form){const r=await p.request.get(path);assert.equal(r.status(),200);const token=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];const saved=await p.request.post(path,{form:{...form,__RequestVerificationToken:token}});assert.equal(saved.status(),200);return saved;}
async function submit(p,f){await Promise.all([p.waitForNavigation({timeout:20000}),f.locator('button').first().click()]);}
async function axeCheck(p){await p.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});assert.deepEqual(await p.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id)),[]);}
(async()=>{
    const b=await chromium.launch({channel:'msedge'});
    try{
        const p=await b.newPage({baseURL});p.on('dialog',d=>d.accept());
        const health=await p.request.get('/health/ready');assert.equal(health.status(),200);assert.match(health.headers().server,/Microsoft-IIS/);
        await post(p,'/Login',{EmployeeNumber:'admin',Password:'admin'});await p.goto('/Profile');const language=await p.locator('[name=language]').inputValue();
        try{
            for(const lang of ['hr','en']){
                await post(p,'/Profile?handler=Language',{language:lang});
                await p.goto('/Project');const title='IIS acceptance '+lang+' '+Date.now();
                await p.locator('#Input_Name').fill(title);await p.locator('#Input_Description').fill('');
                await submit(p,p.locator('form').filter({has:p.locator('#Input_Name')}));
                const project=new URL(p.url()).searchParams.get('id');assert.ok(project,'An explicitly empty optional description must save.');
                assert.equal(await p.locator('#Input_Description').inputValue(),'');await axeCheck(p);
                await p.goto('/Issue?projectId='+project);await p.locator('#Input_Title').fill(title);
                await p.locator('#Input_Type').selectOption('Zadatak');await p.locator('#Input_Status').selectOption('Novo');await p.locator('#Input_Priority').selectOption('Normalan');
                await submit(p,p.locator('form[action*="handler=Save"]'));const issue=new URL(p.url()).searchParams.get('id');assert.ok(issue);
                const comment=p.locator('form[action*="handler=Comment"]');await comment.locator('textarea').fill(title+' comment');await submit(p,comment);
                assert.ok((await p.locator('main').innerText()).includes(title+' comment'));
                const time=p.locator('form[action*="handler=Time"]');await time.locator('[name=hours]').fill('1');await submit(p,time);
                assert.equal(await p.locator('.issue-time-table tbody tr').count(),1);await axeCheck(p);
                await p.goto('/Search');await p.locator('#Query').fill(title);await submit(p,p.locator('form[method=get]'));
                await p.locator('a[href^="/Issue?"]').filter({hasText:title}).first().click();
                assert.equal(new URL(p.url()).pathname,'/Issue');assert.ok(new URL(p.url()).searchParams.get('ReturnUrl').startsWith('/Search?'));
                const csv=await p.request.get('/Issues?ProjectId='+project+'&handler=Csv');assert.equal(csv.status(),200);
                assert.match(csv.headers()['content-disposition'],lang==='hr'?/zadaci\.csv/:/issues\.csv/);
                const missing=await p.request.get('/missing-iis-acceptance');assert.equal(missing.status(),404);const html=await missing.text();assert.ok(!html.includes('IIS 10.0 Detailed Error'));assert.ok(html.includes(lang==='hr'?'Stranica nije dostupna.':'Page unavailable.'));
                await p.setViewportSize({width:320,height:850});await p.goto('/TimeReport?ProjectId='+project);assert.ok(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await axeCheck(p);
                await p.setViewportSize({width:1280,height:900});
            }
        }finally{await post(p,'/Profile?handler=Language',{language});}
        console.log('PASS: actual IIS HTTPS, HR/EN native empty-description project creation, issue/comment/time/search/CSV, localized 404 passthrough, mobile report and axe.');
    }finally{await b.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
