// Requires an already running NVDA with speech debug logging and an unlocked
// desktop. Assertions inspect real NVDA output; DOM/axe cannot substitute for it.
const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL='https://localhost:7443',logFile=process.env.TEAM_MANAGER_NVDA_LOG;
assert.ok(logFile&&fs.existsSync(logFile),'Set TEAM_MANAGER_NVDA_LOG to the active test NVDA speech log.');
const evidence=[];const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function speech(label,action,expected){
    const offset=fs.readFileSync(logFile,'utf8').length;await action();
    for(let i=0;i<30;i++){
        const lines=fs.readFileSync(logFile,'utf8').slice(offset).split(/\r?\n/).filter(x=>x.startsWith('Speaking '));
        const match=lines.find(x=>expected.test(x));
        if(match){evidence.push({label,speech:match});console.log('PASS NVDA '+label+': '+match.slice(0,240));return;}
        await delay(200);
    }
    throw new Error('NVDA speech was not verified: '+label);
}
async function post(p,path,form){const r=await p.request.get(path);assert.equal(r.status(),200);const token=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];const saved=await p.request.post(path,{form:{...form,__RequestVerificationToken:token}});assert.equal(saved.status(),200);return saved;}
(async()=>{
    const b=await chromium.launch({channel:'msedge',headless:false,args:['--force-renderer-accessibility']});
    let original='hr',project;
    try{
        for(const language of ['hr','en']){
            const en=language==='en',ctx=await b.newContext({baseURL,locale:en?'en-GB':'hr-HR'}),p=await ctx.newPage();
            p.on('dialog',d=>d.accept());
            await p.goto('/Login');await p.bringToFront();await delay(2500);assert.equal(await p.evaluate(()=>document.hasFocus()),true);
            const ready=await p.request.get('/health/ready');assert.match(ready.headers().server,/Microsoft-IIS/);
            await speech(language+' skip link',()=>p.locator('.skip-link').focus(),en?/Skip to main content/:/Preskoči na glavni sadržaj/);
            await p.keyboard.press('Enter');assert.equal(await p.evaluate(()=>document.activeElement.id),'main-content');
            await speech(language+' employee label',()=>p.locator('#EmployeeNumber').focus(),en?/Employee number/:/Broj radnika/);
            await p.locator('#EmployeeNumber').fill('nvda-invalid-local');
            await speech(language+' password label',()=>p.locator('#Password').focus(),en?/Password/:/Lozinka/);
            await p.locator('#Password').fill('invalid-test-password');
            await speech(language+' sign-in validation',async()=>{await Promise.all([p.waitForNavigation(),p.locator('main form button').click()]);},en?/Sign-in failed/:/Neispravna prijava/);
            assert.equal(await p.evaluate(()=>document.activeElement.id),'sign-in-errors');
            await p.locator('#EmployeeNumber').fill('admin');await p.locator('#Password').fill('admin');await Promise.all([p.waitForNavigation(),p.locator('main form button').click()]);
            await p.goto('/Profile');if(!project){original=await p.locator('[name=language]').inputValue();const r=await post(p,'/Project',{'Input.Name':'NVDA automated IIS '+Date.now(),'Input.Description':'','Input.State':'Active','Input.Revision':'0'});project=new URL(r.url()).searchParams.get('id');assert.ok(project);}
            await post(p,'/Profile?handler=Language',{language});await p.goto('/Projects');await p.bringToFront();
            await speech(language+' projects link',()=>p.locator('nav a[href="/Projects"]').focus(),en?/'Projects'/:/'Projekti'/);
            await p.goto('/Issue?projectId='+project);await p.bringToFront();
            await speech(language+' issue title',()=>p.locator('#Input_Title').focus(),en?/'Title'/:/'Naslov'/);
            const title='NVDA IIS '+language+' '+Date.now();await p.locator('#Input_Title').fill(title);await p.locator('#Input_Type').selectOption('Zadatak');await p.locator('#Input_Status').selectOption('Novo');await p.locator('#Input_Priority').selectOption('Normalan');
            const save=p.locator('form[action*="handler=Save"] button');await speech(language+' save issue',()=>save.focus(),en?/Save issue/:/Spremi zadatak/);await Promise.all([p.waitForNavigation(),save.click()]);assert.ok(new URL(p.url()).searchParams.get('id'));
            const comment=p.locator('form[action*="handler=Comment"]');await speech(language+' comment',()=>comment.locator('textarea').focus(),en?/'Text'/:/'Tekst'/);await comment.locator('textarea').fill(title+' comment');await Promise.all([p.waitForNavigation(),comment.locator('button').click()]);assert.ok((await p.locator('main').innerText()).includes(title+' comment'));
            const time=p.locator('form[action*="handler=Time"]');await speech(language+' hours and help',()=>time.locator('[name=hours]').focus(),en?/Hours Enter a date/:/Sati Unesi datum/);await time.locator('[name=hours]').fill('1');await Promise.all([p.waitForNavigation(),time.locator('button').click()]);assert.equal(await p.locator('.issue-time-table tbody tr').count(),1);
            await p.goto('/Search');await speech(language+' search field',()=>p.locator('#Query').focus(),en?/Search term/:/'Pojam'/);await p.locator('#Query').fill(title);await Promise.all([p.waitForNavigation(),p.locator('form[method=get] button').click()]);
            const result=p.getByRole('link',{name:title,exact:true}).first();await speech(language+' search result',()=>result.focus(),new RegExp(title));await result.click();assert.ok(new URL(p.url()).searchParams.get('ReturnUrl').startsWith('/Search?'));
            const logout=p.locator('form[action="/Logout"] button');await speech(language+' logout',()=>logout.focus(),en?/Sign out/:/Odjava/);await Promise.all([p.waitForNavigation(),logout.click()]);await p.goto('/Issues');assert.equal(new URL(p.url()).pathname,'/Login');await ctx.close();
        }
        console.log('PASS: real NVDA HR/EN IIS speech and browser actions; '+evidence.length+' speech assertions.');
    }finally{
        try{const p=await b.newPage({baseURL});await post(p,'/Login',{EmployeeNumber:'admin',Password:'admin'});await post(p,'/Profile?handler=Language',{language:original});}finally{fs.writeFileSync(process.env.TEAM_MANAGER_NVDA_EVIDENCE||'artifacts/nvda-iis-evidence.json',JSON.stringify(evidence,null,2));await b.close();}
    }
})().catch(e=>{console.error(e);process.exitCode=1;});
