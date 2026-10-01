-- mvchat · Passo 4: numeri WhatsApp per palestra, template, messaggi, eventi ricevuti da Meta

-- Un numero per palestra. IsSimulated=1: nessun contatto con Meta, serve per prove e dimostrazioni.
CREATE TABLE IF NOT EXISTS WaNumbers (
    Id               INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId   INT NOT NULL,
    GymId            INT NOT NULL,
    DisplayPhone     VARCHAR(30)  NOT NULL,
    DisplayName      VARCHAR(100) NULL,
    PhoneNumberId    VARCHAR(40)  NULL,
    WabaId           VARCHAR(40)  NULL,
    AccessTokenEnc   TEXT NULL,
    IsSimulated      TINYINT(1) NOT NULL DEFAULT 1,
    Status           VARCHAR(20) NOT NULL DEFAULT 'attivo',
    QualityRating    VARCHAR(20) NULL,
    MessagingLimit   VARCHAR(40) NULL,
    LastError        VARCHAR(500) NULL,
    LastCheckAt      DATETIME NULL,
    CreatedAt        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UQ_Wa_Gym (GymId),
    UNIQUE KEY UQ_Wa_PhoneNumberId (PhoneNumberId),
    CONSTRAINT FK_Wa_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id),
    CONSTRAINT FK_Wa_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Template: il primo messaggio di ogni campagna. Va approvato da Meta prima dell'uso.
-- Il testo usa i segnaposto di mvchat ({{nome}}, ...); Variables ricorda l'ordine con cui diventano {{1}}, {{2}} per Meta.
CREATE TABLE IF NOT EXISTS WaTemplates (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    GymId          INT NOT NULL,
    WaNumberId     INT NOT NULL,
    GoalModelId    INT NULL,
    Name           VARCHAR(100) NOT NULL,
    Language       VARCHAR(10)  NOT NULL DEFAULT 'it',
    Category       VARCHAR(20)  NOT NULL DEFAULT 'MARKETING',
    Body           VARCHAR(1024) NOT NULL,
    Variables      VARCHAR(400) NOT NULL DEFAULT '[]',
    Status         VARCHAR(20)  NOT NULL DEFAULT 'bozza',
    MetaTemplateId VARCHAR(40)  NULL,
    RejectReason   VARCHAR(500) NULL,
    CreatedBy      INT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY UQ_Tpl_Number_Name (WaNumberId, Name, Language),
    KEY IX_Tpl_Gym (GymId),
    CONSTRAINT FK_Tpl_Number FOREIGN KEY (WaNumberId) REFERENCES WaNumbers(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Ogni messaggio inviato o ricevuto, con il suo stato (inviato, consegnato, letto, errore).
CREATE TABLE IF NOT EXISTS WaMessages (
    Id             BIGINT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    GymId          INT NOT NULL,
    WaNumberId     INT NOT NULL,
    ContactPhone   VARCHAR(20) NOT NULL,
    Direction      VARCHAR(3)  NOT NULL,
    Kind           VARCHAR(12) NOT NULL,
    Body           TEXT NULL,
    TemplateName   VARCHAR(100) NULL,
    MetaMessageId  VARCHAR(120) NULL,
    Status         VARCHAR(12) NOT NULL,
    Error          VARCHAR(500) NULL,
    SentBy         INT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    StatusAt       DATETIME NULL,
    UNIQUE KEY UQ_Msg_Meta (MetaMessageId),
    KEY IX_Msg_Number_Phone (WaNumberId, ContactPhone, CreatedAt),
    KEY IX_Msg_Gym_At (GymId, CreatedAt),
    CONSTRAINT FK_Msg_Number FOREIGN KEY (WaNumberId) REFERENCES WaNumbers(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Copia grezza di ciò che arriva da Meta: serve per capire un problema senza perdere dati.
CREATE TABLE IF NOT EXISTS WaWebhookEvents (
    Id          BIGINT AUTO_INCREMENT PRIMARY KEY,
    ReceivedAt  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    Payload     MEDIUMTEXT NOT NULL,
    Processed   TINYINT(1) NOT NULL DEFAULT 0,
    Error       VARCHAR(500) NULL,
    KEY IX_Webhook_At (ReceivedAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
