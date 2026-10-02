-- mvchat · Passo 10: informativa privacy per gruppo e attività

ALTER TABLE Organizations ADD COLUMN PrivacyUrl VARCHAR(300) NULL;
GO
ALTER TABLE Gyms ADD COLUMN PrivacyUrl VARCHAR(300) NULL;
GO
CREATE INDEX IX_Conv_LastMessage ON Conversations (LastMessageAt);
GO
