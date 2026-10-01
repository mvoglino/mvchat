-- mvchat · Passo 2: liste contatti, contatti, scarti dell'import, lista STOP, abbinamento colonne

CREATE TABLE IF NOT EXISTS ContactLists (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    GymId          INT NOT NULL,
    Name           VARCHAR(150) NOT NULL,
    FileName       VARCHAR(255) NULL,
    RowsRead       INT NOT NULL DEFAULT 0,
    ValidCount     INT NOT NULL DEFAULT 0,
    NoConsent      INT NOT NULL DEFAULT 0,
    BadPhone       INT NOT NULL DEFAULT 0,
    Duplicates     INT NOT NULL DEFAULT 0,
    OptedOut       INT NOT NULL DEFAULT 0,
    CreatedBy      INT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY IX_Lists_Gym (GymId, CreatedAt),
    KEY IX_Lists_Org (OrganizationId),
    CONSTRAINT FK_List_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id),
    CONSTRAINT FK_List_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

CREATE TABLE IF NOT EXISTS Contacts (
    Id             BIGINT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    GymId          INT NOT NULL,
    ListId         INT NOT NULL,
    FirstName      VARCHAR(100) NOT NULL,
    LastName       VARCHAR(100) NULL,
    Phone          VARCHAR(20)  NOT NULL,
    Email          VARCHAR(200) NULL,
    Membership     VARCHAR(100) NULL,
    ExpiresOn      DATE NULL,
    ConsentDate    DATE NULL,
    ConsentSource  VARCHAR(100) NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UQ_Contact_List_Phone (ListId, Phone),
    KEY IX_Contacts_Gym_Phone (GymId, Phone),
    CONSTRAINT FK_Contact_List FOREIGN KEY (ListId) REFERENCES ContactLists(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Righe scartate durante l'import, con il motivo: servono alla palestra per correggere il file.
CREATE TABLE IF NOT EXISTS ContactRejects (
    Id         BIGINT AUTO_INCREMENT PRIMARY KEY,
    ListId     INT NOT NULL,
    RowNumber  INT NOT NULL,
    Name       VARCHAR(200) NULL,
    Phone      VARCHAR(60)  NULL,
    Reason     VARCHAR(30)  NOT NULL,
    KEY IX_Rejects_List (ListId),
    CONSTRAINT FK_Reject_List FOREIGN KEY (ListId) REFERENCES ContactLists(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Lista STOP: chi ha chiesto di non essere contattato. Vale per tutta la catena (titolare dei dati).
CREATE TABLE IF NOT EXISTS OptOuts (
    Id             BIGINT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    GymId          INT NULL,
    Phone          VARCHAR(20)  NOT NULL,
    Reason         VARCHAR(200) NULL,
    Source         VARCHAR(30)  NOT NULL,
    CreatedBy      INT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UQ_OptOut_Org_Phone (OrganizationId, Phone),
    CONSTRAINT FK_OptOut_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Ultimo abbinamento colonne usato da ogni palestra, così il file del gestionale si riconosce da solo.
CREATE TABLE IF NOT EXISTS ColumnMappings (
    GymId       INT PRIMARY KEY,
    MappingJson VARCHAR(4000) NOT NULL,
    UpdatedAt   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT FK_Mapping_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
