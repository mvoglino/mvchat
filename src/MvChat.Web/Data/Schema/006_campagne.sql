-- mvchat · Passo 6: campagne con coda di invio, orari di invio per palestra, limiti Meta

-- Orari in cui una palestra permette l'invio dei primi messaggi (ora italiana).
-- Nessuna riga per una palestra = invio consentito 24 ore su 24, tutti i giorni.
-- DayOfWeek: 1 = lunedì … 7 = domenica. Minuti dalla mezzanotte; EndMinute escluso (1440 = fino a mezzanotte).
CREATE TABLE IF NOT EXISTS GymSendWindows (
    Id          INT AUTO_INCREMENT PRIMARY KEY,
    GymId       INT NOT NULL,
    DayOfWeek   TINYINT NOT NULL,
    StartMinute SMALLINT NOT NULL,
    EndMinute   SMALLINT NOT NULL,
    KEY IX_Window_Gym (GymId, DayOfWeek),
    CONSTRAINT FK_Window_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Status: bozza · programmata · in_corso · in_pausa · completata · annullata
CREATE TABLE IF NOT EXISTS Campaigns (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    GymId          INT NOT NULL,
    Name           VARCHAR(150) NOT NULL,
    ListId         INT NULL,
    ListName       VARCHAR(150) NOT NULL,
    GoalModelId    INT NOT NULL,
    OfferId        INT NULL,
    WaNumberId     INT NOT NULL,
    TemplateId     INT NOT NULL,
    Status         VARCHAR(12) NOT NULL DEFAULT 'bozza',
    PauseReason    VARCHAR(300) NULL,
    StartAt        DATETIME NULL,
    DailyLimit     INT NULL,
    CreatedBy      INT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    StartedAt      DATETIME NULL,
    CompletedAt    DATETIME NULL,
    LastRunAt      DATETIME NULL,
    LastRunNote    VARCHAR(300) NULL,
    KEY IX_Camp_Gym (GymId, CreatedAt),
    KEY IX_Camp_Status (Status),
    CONSTRAINT FK_Camp_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id),
    CONSTRAINT FK_Camp_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- I destinatari sono copiati dalla lista quando la campagna viene creata:
-- se la lista viene cancellata o ricaricata, la campagna resta com'era.
-- Status: in_attesa · in_invio · inviato · saltato · errore
CREATE TABLE IF NOT EXISTS CampaignRecipients (
    Id             BIGINT AUTO_INCREMENT PRIMARY KEY,
    CampaignId     INT NOT NULL,
    ContactId      BIGINT NULL,
    Phone          VARCHAR(20)  NOT NULL,
    FirstName      VARCHAR(100) NOT NULL,
    LastName       VARCHAR(100) NULL,
    Membership     VARCHAR(100) NULL,
    ExpiresOn      DATE NULL,
    Status         VARCHAR(10)  NOT NULL DEFAULT 'in_attesa',
    Reason         VARCHAR(300) NULL,
    ClaimToken     CHAR(32) NULL,
    ClaimedAt      DATETIME NULL,
    ConversationId BIGINT NULL,
    SentAt         DATETIME NULL,
    UNIQUE KEY UQ_Recipient (CampaignId, Phone),
    KEY IX_Recipient_Status (CampaignId, Status, Id),
    KEY IX_Recipient_Claim (ClaimToken),
    CONSTRAINT FK_Recipient_Camp FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

CREATE INDEX IX_Conv_Campaign ON Conversations (CampaignId);
GO
