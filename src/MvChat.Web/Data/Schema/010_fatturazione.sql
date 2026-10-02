-- mvchat · Passo 9: abbonamenti per attività e rendiconti mensili da fatturare

-- Canone di ogni attività: vuoto = canone di base delle impostazioni. Dal/al: periodo in cui l'abbonamento è attivo.
ALTER TABLE Gyms
    ADD COLUMN MonthlyFeeEur DECIMAL(10,2) NULL,
    ADD COLUMN FeeStartsOn   DATE NULL,
    ADD COLUMN FeeEndsOn     DATE NULL;
GO

-- Rendiconto di un mese chiuso, intestato a chi riceve la fattura: il gruppo, oppure l'attività singola
-- (che ha il suo contenitore in Organizations). Righe, cambio e ricarico restano come erano alla chiusura.
CREATE TABLE IF NOT EXISTS BillingStatements (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    Month          CHAR(7) NOT NULL,
    OrganizationId INT NOT NULL,
    RecipientName  VARCHAR(200) NOT NULL,
    LegalName      VARCHAR(200) NULL,
    VatNumber      VARCHAR(20)  NULL,
    Address        VARCHAR(400) NULL,
    Email          VARCHAR(200) NULL,
    LinesJson      MEDIUMTEXT NOT NULL,
    Subtotal       DECIMAL(12,2) NOT NULL,
    VatPct         DECIMAL(5,2)  NOT NULL,
    VatAmount      DECIMAL(12,2) NOT NULL,
    Total          DECIMAL(12,2) NOT NULL,
    UsdToEur       DECIMAL(10,6) NOT NULL,
    AiMarkupPct    DECIMAL(6,2)  NOT NULL,
    ClosedAt       DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ClosedBy       INT NULL,
    UNIQUE KEY UQ_Statement_Month_Org (Month, OrganizationId),
    CONSTRAINT FK_Statement_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
