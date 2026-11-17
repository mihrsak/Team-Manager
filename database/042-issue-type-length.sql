SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE Version=42)
BEGIN
    DECLARE @TransitionTypeFk sysname=(
        SELECT name FROM sys.foreign_keys
        WHERE parent_object_id=OBJECT_ID(N'dbo.WorkflowTransitions')
          AND referenced_object_id=OBJECT_ID(N'dbo.IssueTypes')
    );
    DECLARE @TransitionPk sysname=(SELECT name FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.WorkflowTransitions') AND type='PK');
    DECLARE @TypePk sysname=(SELECT name FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.IssueTypes') AND type='PK');
    DECLARE @DropConstraints nvarchar(max)=
        N'ALTER TABLE dbo.WorkflowTransitions DROP CONSTRAINT '+QUOTENAME(@TransitionTypeFk)+N';'+
        N'ALTER TABLE dbo.WorkflowTransitions DROP CONSTRAINT '+QUOTENAME(@TransitionPk)+N';'+
        N'ALTER TABLE dbo.IssueTypes DROP CONSTRAINT '+QUOTENAME(@TypePk)+N';';
    ALTER TABLE dbo.Issues DROP CONSTRAINT FK_Issue_Type;
    -- Drop both references before rebuilding the type key.
    EXEC sys.sp_executesql @DropConstraints;
    ALTER TABLE dbo.IssueTypes ALTER COLUMN Name nvarchar(50) NOT NULL;
    ALTER TABLE dbo.Issues ALTER COLUMN Type nvarchar(50) NOT NULL;
    ALTER TABLE dbo.WorkflowTransitions ALTER COLUMN Type nvarchar(50) NOT NULL;
    ALTER TABLE dbo.IssueTypes ADD CONSTRAINT PK_IssueTypes PRIMARY KEY(Name);
    ALTER TABLE dbo.WorkflowTransitions ADD CONSTRAINT PK_WorkflowTransitions PRIMARY KEY(RoleName,Type,FromStatus,ToStatus);
    ALTER TABLE dbo.WorkflowTransitions ADD CONSTRAINT FK_WorkflowTransitions_IssueType FOREIGN KEY(Type) REFERENCES dbo.IssueTypes(Name);
    ALTER TABLE dbo.Issues ADD CONSTRAINT FK_Issue_Type FOREIGN KEY(Type) REFERENCES dbo.IssueTypes(Name);
    INSERT dbo.SchemaVersions(Version) VALUES(42);
END;
COMMIT;
