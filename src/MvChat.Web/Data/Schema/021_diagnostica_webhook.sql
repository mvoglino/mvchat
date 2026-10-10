-- mvchat · Diagnostica degli avvisi di Meta: una nota per gli avvisi ricevuti ma non usati (numero non collegato, mai verificato…)
ALTER TABLE WaWebhookEvents ADD COLUMN Note VARCHAR(300) NULL;
GO
