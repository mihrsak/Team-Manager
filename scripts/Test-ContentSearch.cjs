const assert=require('node:assert/strict');
const {chromium}=require('../artifacts/qa-browser/playwright');
const baseURL=process.env.TEAM_MANAGER_ORACLE_URL||'https://localhost:7606';
async function token(page,path){const response=await page.request.get(path);assert.equal(response.status(),200,path);const value=(await response.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];assert.ok(value);return value;}
async function post(page,path,fields){const response=await page.request.post(path,{form:{...fields,__RequestVerificationToken:await token(page,path)}});assert.equal(response.status(),200,path);return response;}
(async()=>{const browser=await chromium.launch({channel:'msedge'});try{
 const admin=await browser.newPage({baseURL});await post(admin,'/Login',{EmployeeNumber:'admin',Password:'admin'});
 const suffix=Date.now();const needle='ŽSearch_%_'+suffix;
 const projectResponse=await post(admin,'/Project',{'Input.Name':'Content fixture '+suffix,'Input.Revision':'0','Input.State':'Active','Input.IsPublic':'false'});
 const project=new URL(projectResponse.url()).searchParams.get('id');assert.ok(Number(project)>0);
 const titles=['Wiki proof '+suffix,'News proof '+suffix,'Forum proof '+suffix,'Document proof '+suffix];
 const wiki=await post(admin,'/Wiki?projectId='+project+'&handler=Save',{revision:'0',title:titles[0],body:needle});assert.ok(new URL(wiki.url()).searchParams.get('wikiId'));
 await post(admin,'/ProjectNews?projectId='+project+'&handler=Publish',{title:titles[1],body:needle});
 await post(admin,'/Forum?projectId='+project+'&handler=Create',{title:titles[2],body:needle});
 const docPath='/ProjectDocuments?projectId='+project+'&handler=Add';const upload=await admin.request.post(docPath,{multipart:{title:titles[3],description:needle,__RequestVerificationToken:await token(admin,docPath),attachment:{name:'proof.txt',mimeType:'text/plain',buffer:Buffer.from('Binary file content is not indexed')}}});assert.equal(upload.status(),200);
 for(const language of ['en','hr']){
  await post(admin,'/Profile?handler=Language',{language});await admin.goto('/Search?Query='+encodeURIComponent(needle));
  for(const title of titles)assert.ok(await admin.getByRole('link',{name:title,exact:true}).isVisible(),title);
  assert.equal(await admin.locator('html').getAttribute('lang'),language);
 }
 for(let i=0;i<20;i++)await post(admin,'/Wiki?projectId='+project+'&handler=Save',{revision:'0',title:'More '+suffix+' '+i,body:needle});
 await admin.goto('/Search?Query='+encodeURIComponent(needle)+'&Module=wiki');assert.equal(await admin.locator('main section:has(h2) ul li').count(),20);
 await admin.getByRole('link',{name:'Sljedeća stranica',exact:true}).click();await admin.waitForURL('**PageNumber=2**');assert.equal(await admin.locator('main section:has(h2) ul li').count(),1);
 const employee='search'+suffix,password='Content-QA-password-2026';await post(admin,'/Users?handler=Create',{employee,name:employee,password,job:'Search fixture'});await admin.goto('/Users');const profile=await admin.getByRole('row').filter({hasText:employee}).getByRole('link').getAttribute('href');
 const viewer=await browser.newPage({baseURL});await post(viewer,'/Login',{EmployeeNumber:employee,Password:password});
 async function checkVisible(expected){for(const [index,module] of ['wiki','news','forum','documents'].entries()){const response=await viewer.request.get('/Search?Query='+encodeURIComponent(needle)+'&Module='+module+(module==='wiki'?'&PageNumber=2':''));assert.equal(response.status(),200);const html=await response.text();assert.equal(html.includes(titles[index]),expected,'Content search leaked or omitted '+titles[index]);}}
 await checkVisible(false);await post(admin,profile+'&handler=Membership',{kind:'project',scopeId:project,role:'Developer'});await checkVisible(true);
 await post(admin,profile+'&handler=Remove',{kind:'project',scopeId:project});await checkVisible(false);
 console.log('PASS: HR/EN wiki body, document metadata, news/forum content search, literal Unicode/wildcards, result paging and immediate membership revocation.');
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
