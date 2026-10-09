-- mvchat · Link di pagamento e prenotazione, etichette su clienti e conversazioni, promemoria automatico a chi non risponde

-- Link di prenotazione dell'offerta (quello per pagare/aderire c'era già: ActionUrl) e link di prenotazione generale dell'attività.
ALTER TABLE Offers ADD COLUMN BookUrl VARCHAR(400) NULL;
GO
ALTER TABLE GymProfiles ADD COLUMN BookingUrl VARCHAR(400) NULL;
GO

-- Etichette della singola conversazione (si cancellano con la conversazione).
CREATE TABLE IF NOT EXISTS ConversationTags (
    ConversationId BIGINT NOT NULL,
    Tag            VARCHAR(30) NOT NULL,
    CreatedBy      INT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (ConversationId, Tag),
    KEY IX_ConvTags_Tag (Tag),
    CONSTRAINT FK_ConvTags_Conv FOREIGN KEY (ConversationId) REFERENCES Conversations(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
-- Etichette del cliente nell'attività (restano da una campagna all'altra).
CREATE TABLE IF NOT EXISTS ContactTags (
    OrganizationId INT NOT NULL,
    GymId          INT NOT NULL,
    Phone          VARCHAR(20) NOT NULL,
    Tag            VARCHAR(30) NOT NULL,
    CreatedBy      INT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (GymId, Phone, Tag),
    KEY IX_ContactTags_Tag (GymId, Tag),
    CONSTRAINT FK_ContactTags_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Promemoria automatico: un secondo template a chi non ha risposto dopo N giorni (una volta sola).
ALTER TABLE Campaigns ADD COLUMN FollowUpTemplateId INT NULL, ADD COLUMN FollowUpDays TINYINT NOT NULL DEFAULT 3;
GO
-- FollowUpState: invio (prenotato) · inviato · errore · saltato
ALTER TABLE CampaignRecipients ADD COLUMN FollowUpAt DATETIME NULL, ADD COLUMN FollowUpState VARCHAR(10) NULL, ADD COLUMN FollowUpNote VARCHAR(200) NULL;
GO
