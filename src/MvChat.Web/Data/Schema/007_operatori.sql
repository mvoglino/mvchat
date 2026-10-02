-- mvchat · Passo 7: postazione operatori, risposte rapide, archivio dei dialoghi

-- 1 = nella conversazione è intervenuta una persona della palestra (o l'assistente l'ha passata alla reception).
-- Serve all'archivio per dividere i dialoghi "solo assistente AI" da quelli "proseguiti con un operatore".
ALTER TABLE Conversations ADD COLUMN HumanInvolved TINYINT(1) NOT NULL DEFAULT 0;
GO

UPDATE Conversations c SET HumanInvolved = 1
WHERE c.Status = 'operatore' OR c.Outcome = 'operatore' OR c.AssignedUserId IS NOT NULL
   OR EXISTS (SELECT 1 FROM WaMessages m WHERE m.ConversationId = c.Id AND m.Direction = 'out' AND m.Kind = 'text' AND m.SentBy IS NOT NULL);
GO

CREATE INDEX IX_Conv_Human ON Conversations (GymId, HumanInvolved, LastMessageAt);
GO

-- Risposte rapide: testi pronti per la reception. GymId NULL = valida per tutte le palestre della catena.
CREATE TABLE IF NOT EXISTS QuickReplies (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    GymId          INT NULL,
    Title          VARCHAR(80)   NOT NULL,
    Body           VARCHAR(1000) NOT NULL,
    CreatedBy      INT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY IX_Quick_Org (OrganizationId, GymId),
    CONSTRAINT FK_Quick_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
