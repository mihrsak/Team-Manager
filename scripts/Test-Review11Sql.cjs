const {spawnSync}=require('node:child_process'),path=require('node:path');
for(const file of ['Test-NotificationAuditFilters.cjs','Test-RoleGuidance.cjs','Test-RoleRevocationRace.cjs','Test-AccountDrafts.cjs']){
 process.stdout.write('RUN '+file+'\n');const result=spawnSync(process.execPath,[path.join(__dirname,file)],{stdio:'inherit',windowsHide:true,env:process.env});
 if(result.status!==0)process.exit(result.status||1);
}
