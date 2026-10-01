-- mvchat · Passo 1: catene, palestre, utenti, registro attività (MySQL 8 / MariaDB 10.6+)
-- Ogni dato di lavoro è legato a una catena (OrganizationId) e, dove serve, a una palestra (GymId).

CREATE TABLE IF NOT EXISTS Organizations (
    Id            INT AUTO_INCREMENT PRIMARY KEY,
    Name          VARCHAR(150) NOT NULL,
    Slug          VARCHAR(60)  NOT NULL,
    LogoUrl       VARCHAR(400) NULL,
    PrimaryColor  VARCHAR(9)   NOT NULL DEFAULT '#F6931E',
    VatNumber     VARCHAR(20)  NULL,
    BillingEmail  VARCHAR(200) NULL,
    IsActive      TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAt     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UQ_Org_Slug (Slug)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

CREATE TABLE IF NOT EXISTS Gyms (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    Name           VARCHAR(150) NOT NULL,
    City           VARCHAR(100) NULL,
    Address        VARCHAR(250) NULL,
    Phone          VARCHAR(40)  NULL,
    IsActive       TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY IX_Gyms_Org (OrganizationId),
    CONSTRAINT FK_Gym_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Role: superadmin (MVitalia) · orgadmin (direzione catena) · manager (responsabile palestra) · operator (reception)
CREATE TABLE IF NOT EXISTS Users (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NULL,
    GymId          INT NULL,
    Email          VARCHAR(200) NOT NULL,
    FullName       VARCHAR(150) NOT NULL,
    PasswordHash   VARCHAR(400) NOT NULL,
    Role           VARCHAR(20)  NOT NULL,
    IsActive       TINYINT(1) NOT NULL DEFAULT 1,
    FailedLogins   INT NOT NULL DEFAULT 0,
    LockedUntil    DATETIME NULL,
    MustChangePassword TINYINT(1) NOT NULL DEFAULT 0,
    LastLoginAt    DATETIME NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UQ_User_Email (Email),
    KEY IX_Users_Org (OrganizationId),
    KEY IX_Users_Gym (GymId),
    CONSTRAINT FK_User_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id),
    CONSTRAINT FK_User_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

CREATE TABLE IF NOT EXISTS AuditLog (
    Id             BIGINT AUTO_INCREMENT PRIMARY KEY,
    At             DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UserId         INT NULL,
    OrganizationId INT NULL,
    GymId          INT NULL,
    Action         VARCHAR(60)   NOT NULL,
    Detail         VARCHAR(1000) NULL,
    Ip             VARCHAR(45)   NULL,
    KEY IX_Audit_Org_At (OrganizationId, At)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
