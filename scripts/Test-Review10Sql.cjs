const {spawnSync}=require('node:child_process');
for(const file of ['Test-RoleRevocationRace.cjs','Test-AdministrationValidation.cjs','Test-RepositoryEmailDrafts.cjs','Test-TimeAdminRecovery.cjs']){
 process.stdout.write('RUN '+file+'\n');
 const result=spawnSync(process.execPath,[require('node:path').join(__dirname,file)],{stdio:'inherit',windowsHide:true,env:process.env});
 if(result.status!==0)process.exit(result.status||1);
}
