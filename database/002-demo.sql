-- Neobavezni probni podaci. Ne pokretati u produkciji.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.Teams WHERE Name=N'Demo tim')
BEGIN
    INSERT dbo.Teams(Name) VALUES (N'Demo tim');
    DECLARE @TeamId int = SCOPE_IDENTITY();
    INSERT dbo.Members(TeamId,Name) VALUES (@TeamId,N'Demo član');
    DECLARE @MemberId int = SCOPE_IDENTITY();
    INSERT dbo.WorkItems(TeamId,AssigneeId,Title,DueDate)
    VALUES (@TeamId,@MemberId,N'Isprobati Team Manager',DATEADD(day,7,CONVERT(date,GETDATE())));
END;
COMMIT;
