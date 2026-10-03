-- mvchat · Limite di campagne per cliente e numeri di prova per le campagne

-- Per contare in fretta quante campagne ha ricevuto un cliente negli ultimi 30 giorni.
CREATE INDEX IX_Recipient_Phone_Sent ON CampaignRecipients (Phone, SentAt);
GO

-- Numeri di prova dell'attività (titolare, responsabile, reception): ricevono il primo messaggio
-- di una campagna prima del via, per vedere come arriva. Facoltativi.
CREATE TABLE IF NOT EXISTS TestNumbers (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId INT NOT NULL,
    GymId          INT NOT NULL,
    Name           VARCHAR(100) NOT NULL,
    Phone          VARCHAR(20)  NOT NULL,
    CreatedBy      INT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UQ_TestNumber_Gym_Phone (GymId, Phone),
    CONSTRAINT FK_TestNumber_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
