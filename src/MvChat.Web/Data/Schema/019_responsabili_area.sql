-- mvchat · Responsabile di area: un utente del gruppo che segue solo alcune attività (scelte da MVitalia o dall'amministratore di gruppo).
CREATE TABLE IF NOT EXISTS UserGyms (
    UserId INT NOT NULL,
    GymId  INT NOT NULL,
    PRIMARY KEY (UserId, GymId),
    KEY IX_UserGyms_Gym (GymId),
    CONSTRAINT FK_UserGyms_User FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserGyms_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO
