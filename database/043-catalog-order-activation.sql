SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE Version=43)
BEGIN
    ALTER TABLE dbo.IssueTypes ADD SortOrder int NOT NULL CONSTRAINT DF_IssueTypes_Order DEFAULT 100, IsActive bit NOT NULL CONSTRAINT DF_IssueTypes_Active DEFAULT 1;
    ALTER TABLE dbo.IssueStatuses ADD SortOrder int NOT NULL CONSTRAINT DF_IssueStatuses_Order DEFAULT 100, IsActive bit NOT NULL CONSTRAINT DF_IssueStatuses_Active DEFAULT 1;
    ALTER TABLE dbo.IssuePriorities ADD SortOrder int NOT NULL CONSTRAINT DF_IssuePriorities_Order DEFAULT 100, IsActive bit NOT NULL CONSTRAINT DF_IssuePriorities_Active DEFAULT 1;
    EXEC(N'UPDATE dbo.IssuePriorities SET SortOrder=CASE Name WHEN N''Hitno'' THEN 10 WHEN N''Visok'' THEN 20 WHEN N''Normalan'' THEN 30 WHEN N''Nizak'' THEN 40 ELSE 100 END');
    INSERT dbo.SchemaVersions(Version) VALUES(43);
END;
COMMIT;
