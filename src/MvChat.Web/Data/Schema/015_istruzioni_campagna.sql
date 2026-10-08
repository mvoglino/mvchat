-- mvchat · Istruzioni in più per una singola campagna (es. «dal 20 al 27 dicembre siamo chiusi»).
ALTER TABLE Campaigns ADD COLUMN ExtraInstructions VARCHAR(1500) NULL;
GO
-- Le prove inviate da una campagna non contano nella campagna, ma l'assistente deve usarne le istruzioni.
ALTER TABLE Conversations ADD COLUMN TestOfCampaignId INT NULL;
GO
