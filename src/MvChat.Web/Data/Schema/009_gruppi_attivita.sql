-- mvchat · Gerarchia del servizio: MVitalia → gruppi (marchi) → attività → operatori.
-- Un'attività singola ha comunque un "contenitore" in Organizations, con IsGroup = 0 e lo stesso nome:
-- non si vede come gruppo e non può avere un amministratore di gruppo.

ALTER TABLE Organizations ADD COLUMN IsGroup TINYINT(1) NOT NULL DEFAULT 1;
GO

-- Dati propri di ogni attività: tipo (se diverso da quello del gruppo), nome legale, logo, presentazione.
ALTER TABLE Gyms
    ADD COLUMN Sector       VARCHAR(30)  NULL,
    ADD COLUMN LegalName    VARCHAR(200) NULL,
    ADD COLUMN VatNumber    VARCHAR(20)  NULL,
    ADD COLUMN ContactEmail VARCHAR(200) NULL,
    ADD COLUMN Website      VARCHAR(300) NULL,
    ADD COLUMN Description  TEXT NULL,
    ADD COLUMN PrimaryColor VARCHAR(9)   NULL,
    ADD COLUMN LogoData     MEDIUMBLOB   NULL,
    ADD COLUMN LogoType     VARCHAR(30)  NULL,
    ADD COLUMN UpdatedAt    DATETIME     NULL;
GO
