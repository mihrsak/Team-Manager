-- Apply offline through --apply-oracle-concurrency. Sequences are initialized in C#
-- from both imported counters and actual IDs, and are never reset on a retry.
CREATE TABLE IF NOT EXISTS TMR_RUNTIME_FEATURES (NAME VARCHAR2(64) PRIMARY KEY, VERSION NUMBER(4) NOT NULL)
/
CREATE INDEX IF NOT EXISTS TMR_REPORT_TIME_DATE ON "TMR_TimeEntries" ("WorkDate","IssueId","UserId")
/
CREATE INDEX IF NOT EXISTS TMR_REPORT_EVENTS_RECENT ON "TMR_IssueEvents" ("CreatedAt" DESC,"Id" DESC,"IssueId")
/
CREATE INDEX IF NOT EXISTS TMR_REPORT_NOTIFICATIONS ON "TMR_IssueNotifications" ("UserId","IsRead","EventId")
/
CREATE INDEX IF NOT EXISTS TMR_REPORT_ROADMAP ON "TMR_Issues" ("ProjectId","RoadmapItemId","Status")
/
