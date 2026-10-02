-- mvchat · Passo 5: conversazioni guidate dall'assistente AI, esiti, consumi AI

-- Una conversazione = un cliente + un obiettivo. Nasce da una campagna (Passo 6) o da una prova.
-- Status: ai (risponde l'assistente) · operatore (risponde una persona) · chiusa
CREATE TABLE IF NOT EXISTS Conversations (
    Id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId  INT NOT NULL,
    GymId           INT NOT NULL,
    WaNumberId      INT NOT NULL,
    ContactPhone    VARCHAR(20)  NOT NULL,
    ContactName     VARCHAR(150) NOT NULL,
    Membership      VARCHAR(100) NULL,
    ExpiresOn       DATE NULL,
    GoalModelId     INT NOT NULL,
    OfferId         INT NULL,
    CampaignId      INT NULL,
    IsTest          TINYINT(1) NOT NULL DEFAULT 0,
    Status          VARCHAR(12) NOT NULL DEFAULT 'ai',
    Outcome         VARCHAR(24) NOT NULL DEFAULT 'in_corso',
    OutcomeNote     VARCHAR(500) NULL,
    AiReplies       INT NOT NULL DEFAULT 0,
    NeedsReply      TINYINT(1) NOT NULL DEFAULT 0,
    ProcessingSince DATETIME NULL,
    AssignedUserId  INT NULL,
    LastInboundAt   DATETIME NULL,
    LastMessageAt   DATETIME NULL,
    CreatedAt       DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY IX_Conv_Number_Phone (WaNumberId, ContactPhone, CreatedAt),
    KEY IX_Conv_Gym_Status (GymId, Status, LastMessageAt),
    KEY IX_Conv_NeedsReply (NeedsReply),
    CONSTRAINT FK_Conv_Number FOREIGN KEY (WaNumberId) REFERENCES WaNumbers(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

ALTER TABLE WaMessages ADD COLUMN ConversationId BIGINT NULL, ADD KEY IX_Msg_Conv (ConversationId, CreatedAt);
GO

-- Ogni chiamata all'AI con i suoi consumi: serve per rifatturare a consumo (Passo 9).
CREATE TABLE IF NOT EXISTS AiUsage (
    Id               BIGINT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId   INT NULL,
    GymId            INT NULL,
    ConversationId   BIGINT NULL,
    Purpose          VARCHAR(12) NOT NULL,
    Provider         VARCHAR(20) NOT NULL,
    Model            VARCHAR(80) NOT NULL,
    InputTokens      INT NOT NULL DEFAULT 0,
    OutputTokens     INT NOT NULL DEFAULT 0,
    CacheReadTokens  INT NOT NULL DEFAULT 0,
    CacheWriteTokens INT NOT NULL DEFAULT 0,
    CostUsd          DECIMAL(12,6) NOT NULL DEFAULT 0,
    Ok               TINYINT(1) NOT NULL DEFAULT 1,
    Error            VARCHAR(500) NULL,
    CreatedAt        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY IX_Usage_Org_At (OrganizationId, CreatedAt),
    KEY IX_Usage_Gym_At (GymId, CreatedAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
