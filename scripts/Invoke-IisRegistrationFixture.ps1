param([ValidateSet('Seed','State')][string]$Mode,[string]$Employee,[string]$SeedEmployee)
$ErrorActionPreference='Stop'
if($Employee -notmatch '^r12[ae][a-f0-9]{12}(hr|en)$'){throw 'Only generated R12 registration fixture accounts are allowed.'}
$connection=New-Object System.Data.SqlClient.SqlConnection 'Server=localhost\SQLEXPRESS;Database=TeamManager_IisTest;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$connection.Open()
try{
 $command=$connection.CreateCommand();[void]$command.Parameters.AddWithValue('@employee',$Employee)
 if($Mode -eq 'Seed'){
  if($SeedEmployee -notmatch '^r12seed[a-f0-9]{12}$'){throw 'Only the generated QA seed account can supply a fixture password hash.'}
  [void]$command.Parameters.AddWithValue('@seed',$SeedEmployee)
  $command.CommandText="INSERT dbo.RegistrationRequests(EmployeeNumber,DisplayName,PasswordHash) OUTPUT inserted.Id SELECT @employee,N'R12 registration fixture',PasswordHash FROM dbo.AppUsers WHERE EmployeeNumber=@seed;"
  $id=$command.ExecuteScalar();if(!$id){throw 'QA seed account not found.'};Write-Output ([int]$id)
 }else{
  $command.CommandText="SELECT (SELECT COUNT(*) FROM dbo.RegistrationRequests WHERE EmployeeNumber=@employee),(SELECT COUNT(*) FROM dbo.AppUsers WHERE EmployeeNumber=@employee),(SELECT COUNT(*) FROM dbo.AdministrationAudit WHERE Subject=@employee AND Action IN ('registration.approve','registration.reject'));"
  $reader=$command.ExecuteReader();[void]$reader.Read();@{Pending=$reader.GetInt32(0);Accounts=$reader.GetInt32(1);Audit=$reader.GetInt32(2)} | ConvertTo-Json -Compress;$reader.Dispose()
 }
}finally{$connection.Dispose()}
