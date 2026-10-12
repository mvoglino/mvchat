-- A chi va il rendiconto mensile di MVitalia (abbonamento + AI) per un'attività di un gruppo:
-- 'gruppo' (come prima), 'attivita' (l'attività stessa, con i suoi dati fiscali) o 'gestore'
-- (una società che detiene più attività del gruppo e riceve un solo rendiconto per tutte).

CREATE TABLE IF NOT EXISTS BillingEntities (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    Name           VARCHAR(150) NOT NULL,
    LegalName      VARCHAR(200) NULL,
    VatNumber      VARCHAR(20)  NULL,
    Address        VARCHAR(250) NULL,
    City           VARCHAR(100) NULL,
    Email          VARCHAR(200) NULL,
    IsActive       TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY IX_BillingEntities_Org (OrganizationId),
    CONSTRAINT FK_BillingEntity_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

ALTER TABLE Gyms ADD COLUMN BillTo VARCHAR(10) NOT NULL DEFAULT 'gruppo', ADD COLUMN BillingEntityId INT NULL;
GO

-- I rendiconti si distinguono per intestatario: o{gruppo o attività singola}, a{attività del gruppo}, g{gestore}.
ALTER TABLE BillingStatements ADD COLUMN RecipientKey VARCHAR(20) NOT NULL DEFAULT '', ADD COLUMN RecipientKind VARCHAR(10) NOT NULL DEFAULT '';
GO

UPDATE BillingStatements b JOIN Organizations o ON o.Id=b.OrganizationId
SET b.RecipientKey=CONCAT('o', b.OrganizationId), b.RecipientKind=IF(o.IsGroup=1, 'gruppo', 'singola')
WHERE b.RecipientKey='';
GO

ALTER TABLE BillingStatements DROP INDEX UQ_Statement_Month_Org;
GO

ALTER TABLE BillingStatements ADD UNIQUE KEY UQ_Statement_Month_Recipient (Month, RecipientKey);
GO
