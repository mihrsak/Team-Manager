SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=15)
BEGIN
    CREATE TABLE dbo.IssueLinks (
        SourceId int NOT NULL REFERENCES dbo.Issues(Id),
        TargetId int NOT NULL REFERENCES dbo.Issues(Id),
        Kind varchar(12) NOT NULL,
        PRIMARY KEY (SourceId,TargetId,Kind),
        CONSTRAINT CK_IssueLinks_Kind CHECK (Kind IN ('related','duplicate','blocks','parent')),
        CONSTRAINT CK_IssueLinks_Direction CHECK (
            SourceId<>TargetId AND (Kind NOT IN ('related','duplicate') OR SourceId<TargetId))
    );
    CREATE INDEX IX_IssueLinks_Target ON dbo.IssueLinks(TargetId,Kind);
    CREATE UNIQUE INDEX UX_IssueLinks_Parent ON dbo.IssueLinks(TargetId) WHERE Kind='parent';
    INSERT dbo.SchemaVersions(Version) VALUES (15);
END;
COMMIT;
