SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE Version=7)
BEGIN
    CREATE TABLE dbo.IssueTypes(Name nvarchar(30) PRIMARY KEY);
    CREATE TABLE dbo.IssueStatuses(Name nvarchar(30) PRIMARY KEY,IsClosed bit NOT NULL DEFAULT 0);
    CREATE TABLE dbo.IssuePriorities(Name nvarchar(20) PRIMARY KEY);
    INSERT dbo.IssueTypes VALUES(N'Zadatak'),(N'Greška'),(N'Zahtjev');
    INSERT dbo.IssueStatuses VALUES(N'Novo',0),(N'U tijeku',0),(N'Na provjeri',0),(N'Završeno',1);
    INSERT dbo.IssuePriorities VALUES(N'Nizak'),(N'Normalan'),(N'Visok'),(N'Hitno');
    DECLARE @sql nvarchar(max)=N'';
    SELECT @sql=@sql+N'ALTER TABLE dbo.Issues DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Issues');
    EXEC sys.sp_executesql @sql;
    ALTER TABLE dbo.Issues ADD CONSTRAINT FK_Issue_Type FOREIGN KEY(Type) REFERENCES dbo.IssueTypes(Name);
    ALTER TABLE dbo.Issues ADD CONSTRAINT FK_Issue_Status FOREIGN KEY(Status) REFERENCES dbo.IssueStatuses(Name);
    ALTER TABLE dbo.Issues ADD CONSTRAINT FK_Issue_Priority FOREIGN KEY(Priority) REFERENCES dbo.IssuePriorities(Name);
    CREATE TABLE dbo.WorkflowTransitions(
        RoleName nvarchar(30) NOT NULL REFERENCES dbo.Roles(Name),
        Type nvarchar(30) NOT NULL REFERENCES dbo.IssueTypes(Name),
        FromStatus nvarchar(30) NOT NULL REFERENCES dbo.IssueStatuses(Name),
        ToStatus nvarchar(30) NOT NULL REFERENCES dbo.IssueStatuses(Name),
        PRIMARY KEY(RoleName,Type,FromStatus,ToStatus)
    );
    INSERT dbo.WorkflowTransitions
    SELECT r.Name,t.Name,f.Name,s.Name FROM dbo.Roles r CROSS JOIN dbo.IssueTypes t CROSS JOIN dbo.IssueStatuses f CROSS JOIN dbo.IssueStatuses s
    WHERE r.Name IN(N'Developer',N'Voditelj') AND f.Name<>s.Name;
    INSERT dbo.SchemaVersions(Version) VALUES(7);
END;
COMMIT;
