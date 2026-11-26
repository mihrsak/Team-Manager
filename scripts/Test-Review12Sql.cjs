const {spawnSync}=require('node:child_process'),path=require('node:path');
for(const file of ['Test-RoleWorkflowDrafts.cjs','Test-OracleGroups.cjs']){process.stdout.write('RUN '+file+'\n');const r=spawnSync(process.execPath,[path.join(__dirname,file)],{stdio:'inherit',windowsHide:true,env:process.env});if(r.status!==0)process.exit(r.status||1);}
