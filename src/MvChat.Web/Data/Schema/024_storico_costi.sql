-- Storico dei costi e consumi per attività e mese, fissato quando MVitalia chiude il mese in Fatturazione.
-- Da quel momento il contatore della spesa Meta riparte da zero (mese dopo) e il cliente vede lo storico con i valori fissati.
-- Il costo AI (AiUsd) è solo per MVitalia: per le attività è compreso nell'abbonamento.

CREATE TABLE IF NOT EXISTS CostHistory (
    Month             CHAR(7)       NOT NULL,
    GymId             INT           NOT NULL,
    OrganizationId    INT           NOT NULL,
    FeeEur            DECIMAL(10,2) NOT NULL DEFAULT 0,
    MarketingMsgs     INT           NOT NULL DEFAULT 0,
    UtilityMsgs       INT           NOT NULL DEFAULT 0,
    MarketingPriceEur DECIMAL(10,4) NOT NULL DEFAULT 0,
    UtilityPriceEur   DECIMAL(10,4) NOT NULL DEFAULT 0,
    MetaEur           DECIMAL(10,2) NOT NULL DEFAULT 0,
    AiReplies         INT           NOT NULL DEFAULT 0,
    AiUsd             DECIMAL(12,6) NOT NULL DEFAULT 0,
    ClosedAt          DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ClosedBy          INT           NULL,
    PRIMARY KEY (Month, GymId),
    KEY IX_CostHistory_Org (OrganizationId, Month)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
