-- mvchat · Motivo dei rifiuti: quando un cliente dice di no, l'assistente (o l'operatore) sceglie il motivo da un elenco fisso,
-- così il Report può contare i motivi e calcolarne la percentuale nel periodo scelto.
ALTER TABLE Conversations ADD COLUMN RefusalReason VARCHAR(20) NULL;
GO
CREATE INDEX IX_Conv_Outcome_Created ON Conversations (Outcome, CreatedAt);
GO
