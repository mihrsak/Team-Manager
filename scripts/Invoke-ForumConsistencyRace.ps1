param([int]$ActorId,[int]$ProjectId,[int]$TopicId,[ValidateSet('revoke','deactivate')][string]$Mode='revoke')
$ErrorActionPreference='Stop'
$connection=New-Object System.Data.SqlClient.SqlConnection($env:ConnectionStrings__TeamManager);$connection.Open()
if($connection.Database -notmatch '^TeamManager_AccountTest_[0-9a-f]{32}$'){throw 'Use only a generated isolated SQL fixture.'}
$transaction=$connection.BeginTransaction()
try{
 $command=$connection.CreateCommand();$command.Transaction=$transaction
 [void]$command.Parameters.AddWithValue('@actor',$ActorId);[void]$command.Parameters.AddWithValue('@project',$ProjectId);[void]$command.Parameters.AddWithValue('@topic',$TopicId)
 $command.CommandText='SELECT Id FROM dbo.ForumTopics WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id=@topic AND ProjectId=@project'
 if([int]$command.ExecuteScalar() -ne $TopicId){throw 'Own QA topic not found.'}
 [Console]::WriteLine('LOCKED')
 $command.CommandText='SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id=@@SPID AND database_id=DB_ID()'
 $waiting=$false;for($attempt=0;$attempt -lt 60;$attempt++){if([int]$command.ExecuteScalar() -gt 0){$waiting=$true;break};Start-Sleep -Milliseconds 100}
 if(!$waiting){throw 'No blocked QA forum request observed.'}
 $command.CommandText=if($Mode -eq 'revoke'){'DELETE dbo.ProjectMembers WHERE UserId=@actor AND ProjectId=@project'}else{'UPDATE dbo.AppUsers SET IsActive=0 WHERE Id=@actor'}
 if($command.ExecuteNonQuery() -ne 1){throw 'QA mutation did not affect exactly one fixture row.'}
 $transaction.Commit();[Console]::WriteLine('RELEASED_AFTER_CONFIRMED_WAIT')
}finally{$transaction.Dispose();$connection.Dispose()}
