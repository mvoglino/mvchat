-- mvchat · Vocali e foto dei clienti: si scaricano da Meta (per ascoltarli e vederli in reception) e i vocali si trascrivono per l'assistente

-- MediaId: riferimento di Meta al file · MediaMime: tipo di file · MediaFile: nome del file salvato in App_Data/media ('-' = non più disponibile)
-- Transcribed: il vocale è già stato trasformato in testo (il testo è in Body)
ALTER TABLE WaMessages ADD COLUMN MediaId VARCHAR(128) NULL, ADD COLUMN MediaMime VARCHAR(100) NULL, ADD COLUMN MediaFile VARCHAR(120) NULL,
    ADD COLUMN Transcribed TINYINT(1) NOT NULL DEFAULT 0;
GO
