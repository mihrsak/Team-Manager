MERGE INTO TM_OVERVIEW_TASKS target
USING (
    SELECT N'Provjeriti Oracle vezu' TITLE, 'new' STATUS FROM DUAL
    UNION ALL SELECT N'Isprobati promjenu statusa', 'active' FROM DUAL
    UNION ALL SELECT N'Potvrditi čitanje iz Oraclea', 'done' FROM DUAL
) demo
ON (target.TITLE = demo.TITLE)
WHEN NOT MATCHED THEN INSERT (TITLE, STATUS) VALUES (demo.TITLE, demo.STATUS)
