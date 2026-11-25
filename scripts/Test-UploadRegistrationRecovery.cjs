const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL;
assert.ok(['localhost','127.0.0.1'].includes(new URL(baseURL).hostname));
assert.ok(!['7610','7589'].includes(new URL(baseURL).port),'Use an isolated fixture.');
async function post(p,path,form){
    const r=await p.request.get(path);assert.equal(r.status(),200);
    const token=(await r.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];
    const saved=await p.request.post(path,{form:{...form,__RequestVerificationToken:token}});assert.equal(saved.status(),200);return saved;
}
async function axeCheck(p){
    await p.addScriptTag({path:require.resolve('../artifacts/qa-browser/axe/axe.min.js')});
    assert.deepEqual(await p.evaluate(async()=>(await axe.run({runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>v.id)),[]);
}
(async()=>{
    const b=await chromium.launch({channel:'msedge'});
    try{
        const ctx=await b.newContext({baseURL}),p=await ctx.newPage();
        ctx.setDefaultTimeout(15000);ctx.setDefaultNavigationTimeout(15000);
        p.on('dialog',d=>d.accept());
        await post(p,'/Login',{EmployeeNumber:'admin',Password:'admin'});
        await p.goto('/Profile');const original=await p.locator('[name=language]').inputValue();
        const created=await post(p,'/Project',{'Input.Name':'Upload recovery '+Date.now(),'Input.State':'Active','Input.IsPublic':'false','Input.Revision':'0'});
        const project=new URL(created.url()).searchParams.get('id'),path='/ProjectDocuments?projectId='+project;
        try{
            for(const language of ['hr','en']){
                await post(p,'/Profile?handler=Language',{language});
                await p.goto('/InitialPassword');assert.match(await p.title(),language==='hr'?/Početna lozinka/:/Initial password/);
                await p.goto(path);const form=p.locator('form[action*="handler=Add"]'),file=form.locator('[name=attachment]');
                await form.locator('[name=title]').fill('Unsaved '+language);await form.locator('[name=description]').fill('\n\n  My draft '+language+'  ');
                for(const size of [0,10485761,33554432]){
                    await file.setInputFiles({name:'rejected.txt',mimeType:'text/plain',buffer:Buffer.alloc(size)});
                    assert.equal(await file.evaluate(e=>e.validity.customError),true);
                    assert.match(await file.evaluate(e=>e.validationMessage),language==='hr'?/Odaberi datoteku/:/Select a file/);
                    await form.locator('button').click();assert.equal(new URL(p.url()).searchParams.get('handler'),null);
                    assert.equal(await form.locator('[name=title]').inputValue(),'Unsaved '+language);
                    assert.equal(await p.evaluate(()=>!window.dispatchEvent(new Event('beforeunload',{cancelable:true}))),true);
                }
                await file.setInputFiles({name:'boundary.txt',mimeType:'text/plain',buffer:Buffer.alloc(10485760)});
                assert.equal(await file.evaluate(e=>e.checkValidity()),true);
                await file.setInputFiles({name:'valid.txt',mimeType:'text/plain',buffer:Buffer.from('Valid recovery')});
                const before=await p.locator('a[download]').count();
                await Promise.all([p.waitForNavigation(),form.locator('button').click()]);
                assert.equal(await p.locator('a[download]').count(),before+1);await axeCheck(p);

                const plain=await b.newContext({baseURL,javaScriptEnabled:false});await plain.addCookies(await ctx.cookies());
                const nojs=await plain.newPage();await nojs.goto(path);
                const f=nojs.locator('form[action*="handler=Add"]');await f.locator('[name=title]').fill('No JS draft');await f.locator('[name=description]').fill('Keep this text');
                await f.locator('[name=attachment]').setInputFiles({name:'too-large.txt',mimeType:'text/plain',buffer:Buffer.alloc(33554432)});
                const response=nojs.waitForResponse(r=>r.request().method()==='POST');
                const results=await Promise.allSettled([response,nojs.waitForNavigation({timeout:15000}),f.locator('button').click()]);
                assert.equal(results[0].status,'fulfilled');assert.equal(results[0].value.status(),413);
                await nojs.waitForLoadState();assert.match(await nojs.locator('h1').innerText(),language==='hr'?/Zahtjev je prevelik/:/request is too large/);
                assert.equal(await nojs.locator('html').getAttribute('lang'),language);
                await nojs.goBack();assert.equal(await nojs.locator('[name=title]').inputValue(),'No JS draft');assert.equal(await nojs.locator('[name=description]').inputValue(),'Keep this text');
                await plain.close();await p.goto(path);assert.equal(await p.locator('a[download]').count(),before+1);

                const anon=await b.newContext({baseURL,extraHTTPHeaders:{'Accept-Language':language}}),a=await anon.newPage();
                const registration=await a.goto('/Register');
                if(process.argv.includes('--registration-disabled')){assert.equal(registration.status(),404);await anon.close();continue;}
                assert.equal(registration.status(),200,'Enable registration on the isolated SQL fixture.');
                assert.match(await a.title(),language==='hr'?/Zahtjev za registraciju/:/Registration request/);
                await a.locator('[name=employee]').fill('review.recovery');await a.locator('[name=name]').fill('  Review <Example>  ');
                await a.locator('[name=password]').fill('Mismatch-password-A');await a.locator('[name=confirm]').fill('Mismatch-password-B');
                await Promise.all([a.waitForNavigation(),a.locator('form button').click()]);
                assert.equal(await a.locator('[name=employee]').inputValue(),'review.recovery');assert.equal(await a.locator('[name=name]').inputValue(),'  Review <Example>  ');
                assert.equal(await a.locator('[name=password]').inputValue(),'');assert.equal(await a.locator('[name=confirm]').inputValue(),'');
                assert.equal(await a.locator('example').count(),0);assert.equal(await a.evaluate(()=>document.activeElement.id),'validation-errors');
                await axeCheck(a);await anon.close();
            }
        }finally{await post(p,'/Profile?handler=Language',{language:original});}
        console.log('PASS: HR/EN upload boundaries, client draft protection, no-JS HTTP 413 and browser-back recovery, successful corrected upload, initial page titles; '+(process.argv.includes('--registration-disabled')?'disabled registration remains 404.':'registration safe fields/password clearing/focus/axe.'));
    }finally{await b.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
