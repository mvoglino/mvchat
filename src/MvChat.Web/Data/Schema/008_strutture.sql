-- mvchat · Strutture di qualsiasi settore (non solo palestre): tipo di attività, dati aziendali, logo caricato

ALTER TABLE Organizations
    ADD COLUMN Sector       VARCHAR(30)  NOT NULL DEFAULT 'palestra',
    ADD COLUMN LegalName    VARCHAR(200) NULL,
    ADD COLUMN Address      VARCHAR(250) NULL,
    ADD COLUMN City         VARCHAR(100) NULL,
    ADD COLUMN Phone        VARCHAR(40)  NULL,
    ADD COLUMN ContactEmail VARCHAR(200) NULL,
    ADD COLUMN Website      VARCHAR(300) NULL,
    ADD COLUMN Description  TEXT NULL,
    ADD COLUMN LogoData     MEDIUMBLOB NULL,
    ADD COLUMN LogoType     VARCHAR(30) NULL,
    ADD COLUMN UpdatedAt    DATETIME NULL;
GO
