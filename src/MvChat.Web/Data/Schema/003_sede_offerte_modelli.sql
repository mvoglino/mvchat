-- mvchat · Passo 3: scheda sede (senza prezzi), offerte con prezzo per palestra, modelli di obiettivo

-- Le informazioni con cui l'assistente risponde alle domande "di contorno". Niente prezzi:
-- i prezzi stanno solo nelle offerte, così l'AI non può citarne di vecchi o sbagliati.
CREATE TABLE IF NOT EXISTS GymProfiles (
    GymId          INT PRIMARY KEY,
    OpeningHours   VARCHAR(1000) NULL,
    Services       VARCHAR(2000) NULL,
    Classes        VARCHAR(2000) NULL,
    HowToReach     VARCHAR(1000) NULL,
    ExtraInfo      TEXT NULL,
    AssistantName  VARCHAR(60)  NOT NULL DEFAULT 'assistente virtuale',
    Formality      VARCHAR(3)   NOT NULL DEFAULT 'tu',
    UpdatedBy      INT NULL,
    UpdatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT FK_Profile_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- L'oggetto della promo: lo definisce ogni palestra, con il suo prezzo e le sue condizioni.
CREATE TABLE IF NOT EXISTS Offers (
    Id                  INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId      INT NOT NULL,
    GymId               INT NOT NULL,
    Title               VARCHAR(150) NOT NULL,
    Description         VARCHAR(2000) NULL,
    Price               DECIMAL(10,2) NULL,
    FullPrice           DECIMAL(10,2) NULL,
    PriceNote           VARCHAR(100) NULL,
    Conditions          VARCHAR(2000) NULL,
    ValidFrom           DATE NULL,
    ValidTo             DATE NULL,
    MaxExtraDiscountPct TINYINT NOT NULL DEFAULT 0,
    ActionUrl           VARCHAR(400) NULL,
    IsActive            TINYINT(1) NOT NULL DEFAULT 1,
    CreatedBy           INT NULL,
    CreatedAt           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    KEY IX_Offers_Gym (GymId, IsActive),
    CONSTRAINT FK_Offer_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id),
    CONSTRAINT FK_Offer_Gym FOREIGN KEY (GymId) REFERENCES Gyms(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

-- Modelli di obiettivo: OrganizationId NULL = modello standard MVitalia, valido per tutte le catene.
CREATE TABLE IF NOT EXISTS GoalModels (
    Id                 INT AUTO_INCREMENT PRIMARY KEY,
    OrganizationId     INT NULL,
    Code               VARCHAR(40)  NOT NULL,
    Name               VARCHAR(100) NOT NULL,
    Success            VARCHAR(500) NOT NULL,
    Instructions       TEXT NOT NULL,
    TemplateSuggestion VARCHAR(1000) NULL,
    NeedsOffer         TINYINT(1) NOT NULL DEFAULT 1,
    MaxAiMessages      TINYINT NOT NULL DEFAULT 8,
    IsActive           TINYINT(1) NOT NULL DEFAULT 1,
    SortOrder          INT NOT NULL DEFAULT 100,
    CreatedAt          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    KEY IX_Models_Org (OrganizationId),
    CONSTRAINT FK_Model_Org FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, SortOrder)
SELECT NULL, 'rinnovo', 'Rinnovo abbonamento',
 'Il cliente conferma che rinnova, oppure chiede il link per pagare o di passare in reception per farlo.',
 'Il cliente ha un abbonamento in scadenza. Ricordagli con garbo la data di scadenza, presenta l''offerta di rinnovo e i suoi vantaggi, rispondi ai dubbi. Se esita sul prezzo puoi usare lo sconto extra solo entro il limite indicato. Se accetta, conferma e indica come completare (link o reception). Se rifiuta, chiedi una sola volta il motivo e ringrazia.',
 'Ciao {{nome}}, il tuo abbonamento {{abbonamento}} in {{palestra}} scade il {{scadenza}}. Abbiamo un''offerta riservata per il rinnovo: vuoi che ti racconti i dettagli?',
 1, 10
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='rinnovo');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, SortOrder)
SELECT NULL, 'promo', 'Adesione a una promo',
 'Il cliente accetta l''offerta entro la sua scadenza.',
 'Presenta l''offerta in modo semplice: cosa comprende, quanto costa, fino a quando vale. Rispondi alle domande usando solo i dati dell''offerta e della sede. Se il cliente è interessato, indica come aderire. Non mettere fretta oltre la scadenza reale dell''offerta.',
 'Ciao {{nome}}! In {{palestra}} è partita una promozione pensata per chi si allena con noi. Ti interessa sapere di cosa si tratta?',
 1, 20
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='promo');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, SortOrder)
SELECT NULL, 'rientro', 'Recupero inattivi',
 'Il cliente prenota un rientro o una consulenza, oppure dice in quale giorno tornerà.',
 'Il cliente non viene in palestra da tempo. Non rimproverarlo e non chiedere perché in modo insistente. Mostra interesse, ricorda cosa offre la sede (corsi, orari, servizi) e proponi un passo piccolo e concreto: un giorno e un orario per tornare, o la consulenza dell''offerta se presente.',
 'Ciao {{nome}}, è da un po'' che non ti vediamo in {{palestra}}. Ti va di tornare? Abbiamo pensato a qualcosa per ripartire con il piede giusto.',
 0, 30
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='rientro');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, SortOrder)
SELECT NULL, 'amico', 'Porta un amico',
 'Il cliente indica un amico interessato o prenota una prova per lui.',
 'Spiega il vantaggio per il cliente e per l''amico come descritto nell''offerta. Chiedi se c''è qualcuno a cui farebbe piacere provare. Se sì, proponi di prenotare una prova e raccogli solo il nome dell''amico: il numero dell''amico non va chiesto senza il suo consenso.',
 'Ciao {{nome}}, allenarsi in compagnia è più facile! Porta un amico in {{palestra}}: avete un vantaggio tutti e due. Vuoi saperne di più?',
 1, 40
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='amico');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder)
SELECT NULL, 'sondaggio', 'Sondaggio soddisfazione',
 'Il cliente risponde alle tre domande.',
 'Fai tre domande, una alla volta, aspettando la risposta: 1) da 1 a 10 quanto consiglieresti la palestra a un amico; 2) cosa ti piace di più; 3) cosa miglioreresti. Non commentare le critiche, ringrazia. Se emerge un problema concreto, proponi di farlo richiamare dalla reception.',
 'Ciao {{nome}}, ci aiuti a migliorare {{palestra}}? Sono tre domande veloci, meno di un minuto.',
 0, 6, 50
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='sondaggio');
GO
