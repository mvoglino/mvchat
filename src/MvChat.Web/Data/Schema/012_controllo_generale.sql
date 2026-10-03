-- mvchat · Controllo generale (ottobre 2026): correzioni emerse dalla revisione di tutto il programma

-- Messaggi spontanei: un cliente che scrive senza una campagna apre comunque una conversazione per la reception.
ALTER TABLE Conversations MODIFY GoalModelId INT NULL;
GO
-- Numero WhatsApp verificato almeno una volta da Meta: solo da quel momento se ne accettano i messaggi.
ALTER TABLE WaNumbers ADD COLUMN VerifiedAt DATETIME NULL;
GO
UPDATE WaNumbers SET VerifiedAt = COALESCE(LastCheckAt, CreatedAt) WHERE Status = 'attivo' OR IsSimulated = 1;
GO
-- Avvisi di Meta non elaborati per un problema momentaneo: si riprovano qualche volta dall'operazione pianificata.
ALTER TABLE WaWebhookEvents ADD COLUMN Attempts INT NOT NULL DEFAULT 1, ADD KEY IX_Webhook_Pending (Processed, ReceivedAt);
GO
-- Tipi di messaggio più lunghi (es. «interactive», «unsupported»).
ALTER TABLE WaMessages MODIFY Kind VARCHAR(20) NOT NULL;
GO
-- Destinatari: quante volte si è riprovato l'invio dopo un errore momentaneo.
ALTER TABLE CampaignRecipients ADD COLUMN Attempts INT NOT NULL DEFAULT 0;
GO
-- Indici per report, fatturazione, conservazione dei dati e cancellazioni.
CREATE INDEX IX_Usage_Conv ON AiUsage (ConversationId);
GO
CREATE INDEX IX_Audit_Action_At ON AuditLog (Action, At);
GO
CREATE INDEX IX_Audit_At ON AuditLog (At);
GO
CREATE INDEX IX_Conv_Created ON Conversations (CreatedAt);
GO
CREATE INDEX IX_Msg_Created ON WaMessages (CreatedAt);
GO
