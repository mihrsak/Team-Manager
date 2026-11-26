param([int]$ActorId,[string]$ActorRole,[ValidateSet('revoke','deactivate')][string]$Mode='revoke')
$ErrorActionPreference='Stop'
$connection=New-Object System.Data.SqlClient.SqlConnection($env:ConnectionStrings__TeamManager)
$connection.Open()
if($connection.Database -notmatch '^TeamManager_AccountTest_[0-9a-f]{32}$'){throw 'Use only a generated isolated SQL fixture.'}
$transaction=$connection.BeginTransaction()
try{
 $command=$connection.CreateCommand();$command.Transaction=$transaction
 $command.CommandText="DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource='TeamManager.Roles',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=2000; IF @result<0 THROW 51000,'QA lock unavailable',1;"
 [void]$command.ExecuteNonQuery()
 [Console]::WriteLine('LOCKED')
 $command.CommandText='SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id=@@SPID AND database_id=DB_ID()'
 $waiting=$false
 for($attempt=0;$attempt -lt 60;$attempt++){if([int]$command.ExecuteScalar() -gt 0){$waiting=$true;break};Start-Sleep -Milliseconds 100}
 if(!$waiting){throw 'No blocked role request observed.'}
 if($Mode -eq 'revoke'){$command.CommandText='DELETE dbo.UserRoles WHERE UserId=@actor AND RoleName=@role'}else{$command.CommandText='UPDATE dbo.AppUsers SET IsActive=0 WHERE Id=@actor'}
 [void]$command.Parameters.AddWithValue('@actor',$ActorId)
 [void]$command.Parameters.AddWithValue('@role',$ActorRole)
 if($command.ExecuteNonQuery() -ne 1){throw 'QA actor mutation did not affect exactly one fixture row.'}
 $transaction.Commit()
 [Console]::WriteLine('RELEASED_AFTER_CONFIRMED_WAIT')
}finally{$transaction.Dispose();$connection.Dispose()}
