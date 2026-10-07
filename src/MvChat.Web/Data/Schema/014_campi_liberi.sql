-- mvchat · Due informazioni in più per ogni cliente della lista: «Servizio / corso» e «Note».
-- Si possono usare nel primo messaggio ({{servizio}}, {{note}}) e l'assistente le legge per personalizzare il dialogo.
ALTER TABLE Contacts ADD COLUMN Service VARCHAR(150) NULL, ADD COLUMN Notes VARCHAR(300) NULL;
GO
ALTER TABLE CampaignRecipients ADD COLUMN Service VARCHAR(150) NULL, ADD COLUMN Notes VARCHAR(300) NULL;
GO
ALTER TABLE Conversations ADD COLUMN Service VARCHAR(150) NULL, ADD COLUMN Notes VARCHAR(300) NULL;
GO
