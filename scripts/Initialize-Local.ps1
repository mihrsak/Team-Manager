param([switch]$Demo)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Invoke-SqlFile($file, $database) {
    $connection = New-Object System.Data.SqlClient.SqlConnection "Server=localhost\SQLEXPRESS;Database=$database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    try {
        $connection.Open()
        $sql = Get-Content (Join-Path $root "database\$file") -Raw -Encoding UTF8
        foreach ($batch in [regex]::Split($sql, '(?im)^\s*GO\s*$')) {
            if ([string]::IsNullOrWhiteSpace($batch)) { continue }
            $command = $connection.CreateCommand()
            $command.CommandText = $batch
            $command.ExecuteNonQuery() | Out-Null
            $command.Dispose()
        }
    } finally { $connection.Dispose() }
}
Invoke-SqlFile '000-create-database.sql' 'master'
Invoke-SqlFile '001-schema.sql' 'TeamManager'
Invoke-SqlFile '003-users.sql' 'TeamManager'
Invoke-SqlFile '004-roles.sql' 'TeamManager'
Invoke-SqlFile '005-issues.sql' 'TeamManager'
Invoke-SqlFile '007-workflow.sql' 'TeamManager'
Invoke-SqlFile '008-projects.sql' 'TeamManager'
Invoke-SqlFile '009-project-modules.sql' 'TeamManager'
Invoke-SqlFile '010-account-lifecycle.sql' 'TeamManager'
Invoke-SqlFile '011-membership-roles.sql' 'TeamManager'
Invoke-SqlFile '012-user-groups.sql' 'TeamManager'
Invoke-SqlFile '013-project-role-inheritance.sql' 'TeamManager'
Invoke-SqlFile '014-time-entry-activity.sql' 'TeamManager'
Invoke-SqlFile '015-issue-links.sql' 'TeamManager'
Invoke-SqlFile '016-saved-issue-queries.sql' 'TeamManager'
Invoke-SqlFile '017-saved-issue-query-layout.sql' 'TeamManager'
Invoke-SqlFile '018-issue-start-dates.sql' 'TeamManager'
Invoke-SqlFile '019-project-milestones.sql' 'TeamManager'
Invoke-SqlFile '020-issue-roadmap-items.sql' 'TeamManager'
Invoke-SqlFile '021-issue-watchers.sql' 'TeamManager'
if ($Demo) { Invoke-SqlFile '002-demo.sql' 'TeamManager' }
Write-Host 'TeamManager database is ready.'
