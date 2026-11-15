SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=20)
BEGIN
    ALTER TABLE dbo.ProjectMilestones ADD CONSTRAINT UQ_ProjectMilestones_Project_Id UNIQUE (ProjectId,Id);
    ALTER TABLE dbo.Issues ADD RoadmapItemId int NULL;
    EXEC(N'ALTER TABLE dbo.Issues ADD CONSTRAINT FK_Issues_RoadmapItem
        FOREIGN KEY (ProjectId,RoadmapItemId) REFERENCES dbo.ProjectMilestones(ProjectId,Id);
        CREATE INDEX IX_Issues_RoadmapItem ON dbo.Issues(ProjectId,RoadmapItemId,Status)
        WHERE RoadmapItemId IS NOT NULL;');
    INSERT dbo.SchemaVersions(Version) VALUES (20);
END;
COMMIT;
