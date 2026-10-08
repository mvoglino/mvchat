-- mvchat · Chiedere il motivo a chi rifiuta senza dirlo (una sola volta, con garbo): si sceglie campagna per campagna (di base sì).
ALTER TABLE Campaigns ADD COLUMN AskRefusalReason TINYINT(1) NOT NULL DEFAULT 1;
GO
-- 0 = motivo mai chiesto · 1 = chiesto, si aspetta la risposta del cliente · 2 = chiesto e risposta già lavorata (non si richiede)
ALTER TABLE Conversations ADD COLUMN ReasonAsk TINYINT NOT NULL DEFAULT 0;
GO
