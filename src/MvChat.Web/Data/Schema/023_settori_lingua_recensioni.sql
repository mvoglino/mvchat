-- mvchat per attività di ogni settore: lingua del cliente, link per le recensioni, modelli di obiettivo per settore,
-- filtri usati nella creazione della campagna.

ALTER TABLE Contacts ADD COLUMN Language VARCHAR(5) NULL;
GO
ALTER TABLE CampaignRecipients ADD COLUMN Language VARCHAR(5) NULL;
GO
ALTER TABLE Conversations ADD COLUMN Language VARCHAR(5) NULL;
GO

-- Link per lasciare una recensione (es. Google): lo usa il modello «Richiesta di recensione».
ALTER TABLE GymProfiles ADD COLUMN ReviewUrl VARCHAR(400) NULL;
GO

-- Settori a cui è adatto un modello (chiavi separate da virgola, vuoto = tutti).
ALTER TABLE GoalModels ADD COLUMN Sectors VARCHAR(200) NULL;
GO

-- Quali clienti della lista sono stati scelti (date, tipo, servizio, lingua): resta scritto nella campagna.
ALTER TABLE Campaigns ADD COLUMN FilterNote VARCHAR(500) NULL;
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder, Sectors)
SELECT NULL, 'recensione', 'Richiesta di recensione',
 'Il cliente ha ricevuto il link per la recensione oppure ha raccontato com''è andata.',
 'Ringrazia il cliente per essere stato da noi e chiedi com''è andata. Qualunque sia la risposta, ringrazia e invita a lasciare una recensione con il link per le recensioni delle istruzioni: invitano tutti allo stesso modo, senza chiedere di scrivere solo bene e senza offrire premi o sconti in cambio. Se racconta un problema, scusati, prendine nota e chiedi se vuole essere ricontattato dallo staff: se sì, esito operatore. Se nelle istruzioni non c''è il link per le recensioni, non inventarlo: ringrazia e chiudi.',
 'Ciao {{nome}}, grazie per essere stato da noi in {{sede}}! Com''è andata? Ci farebbe piacere sapere cosa ne pensi.',
 0, 4, 60, NULL
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='recensione');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder, Sectors)
SELECT NULL, 'appuntamento', 'Conferma appuntamento',
 'Il cliente conferma che verrà all''appuntamento.',
 'Il cliente ha un appuntamento nella data indicata nei suoi dati; il tipo di servizio e l''orario possono essere nel campo servizio/corso. Chiedi la conferma. Se conferma, ringrazia e ricorda eventuali indicazioni utili della scheda (cosa portare, arrivare qualche minuto prima). Se vuole spostarlo o disdirlo, non proporre tu nuove date: ringrazia, digli che lo ricontatterà la reception e dai esito operatore. Non dare consigli medici e non parlare della salute del cliente.',
 'Ciao {{nome}}, ti ricordiamo l''appuntamento del {{data}} in {{sede}}: {{servizio}}. Puoi confermare rispondendo a questo messaggio?',
 0, 4, 70, 'salute,benessere,servizi,formazione,altro'
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='appuntamento');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder, Sectors)
SELECT NULL, 'richiamo', 'Richiamo per un controllo',
 'Il cliente chiede di fissare il controllo o accetta di essere ricontattato per fissarlo.',
 'È passato del tempo dall''ultima visita o prestazione e conviene un controllo periodico. Ricordalo con garbo, senza allarmare e senza parlare di diagnosi o della salute del cliente. Se vuole prenotare usa il link di prenotazione delle istruzioni, se c''è; altrimenti chiedi quando preferisce essere chiamato e dai esito operatore. Non dare consigli medici.',
 'Ciao {{nome}}, è passato un po'' di tempo dalla tua ultima visita in {{sede}}: vuoi che fissiamo il prossimo controllo?',
 0, 6, 75, 'salute,benessere'
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='richiamo');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder, Sectors)
SELECT NULL, 'ritorno_ospite', 'Ritorno dell''ospite',
 'L''ospite chiede disponibilità o prenota un nuovo soggiorno (anche con il link di prenotazione).',
 'L''ospite ha già soggiornato da noi. Ringrazialo, ricorda in modo semplice cosa offre la struttura nella stagione (dalla scheda) e presenta l''offerta riservata a chi torna. Per date e disponibilità usa il link di prenotazione, se presente; altrimenti chiedi date e numero di persone e dai esito operatore perché la reception confermi. Non confermare mai tu la disponibilità.',
 'Ciao {{nome}}, è stato un piacere averti nostro ospite in {{sede}}! Per il tuo prossimo soggiorno abbiamo pensato a un''offerta riservata: vuoi saperne di più?',
 1, 8, 80, 'hotel'
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='ritorno_ospite');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder, Sectors)
SELECT NULL, 'bassa_stagione', 'Offerta di bassa stagione',
 'Il cliente chiede disponibilità o prenota nelle date dell''offerta.',
 'Presenta l''offerta valida nei periodi più tranquilli: cosa comprende, in quali date vale e il prezzo dell''offerta. Per prenotare usa il link di prenotazione, se presente; altrimenti chiedi date e numero di persone e dai esito operatore. Non confermare mai tu la disponibilità.',
 'Ciao {{nome}}, in {{sede}} abbiamo un''offerta per i prossimi mesi, quando c''è più tranquillità: vuoi che ti racconti?',
 1, 8, 85, 'hotel,ristorazione,benessere'
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='bassa_stagione');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder, Sectors)
SELECT NULL, 'pacchetto', 'Pacchetto trattamenti',
 'Il cliente accetta il pacchetto o chiede di prenotare il primo trattamento.',
 'Presenta il pacchetto dell''offerta: quali trattamenti comprende, quante sedute, fino a quando vale e il prezzo. Rispondi alle domande con i dati dell''offerta e della scheda. Se è interessato, indica come aderire o prenotare (link o reception).',
 'Ciao {{nome}}, in {{sede}} abbiamo preparato un pacchetto di trattamenti a un prezzo dedicato: vuoi che ti dica cosa comprende?',
 1, 8, 90, 'benessere'
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='pacchetto');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder, Sectors)
SELECT NULL, 'evento', 'Serata o evento',
 'Il cliente prenota o chiede di riservare un posto per l''evento.',
 'Presenta l''evento dell''offerta: data, cosa è previsto, prezzo se indicato. Se il cliente vuole partecipare, usa il link di prenotazione, se presente; altrimenti chiedi per quante persone e dai esito operatore perché la reception confermi il posto.',
 'Ciao {{nome}}, in {{sede}} stiamo preparando una serata speciale e ci farebbe piacere averti con noi: vuoi i dettagli?',
 1, 8, 95, 'ristorazione,hotel,negozio'
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='evento');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder, Sectors)
SELECT NULL, 'nuovo_corso', 'Iscrizione al nuovo corso',
 'Il cliente si iscrive o chiede di riservare un posto nel nuovo corso.',
 'Presenta il nuovo corso dell''offerta: a chi è adatto, quando inizia, giorni e orari, durata e prezzo. Rispondi alle domande con i dati dell''offerta e della scheda. Se è interessato, indica come iscriversi (link o segreteria).',
 'Ciao {{nome}}, in {{sede}} sta per partire un nuovo corso che potrebbe interessarti: vuoi saperne di più?',
 1, 8, 100, 'formazione,palestra'
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='nuovo_corso');
GO

INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, SortOrder, Sectors)
SELECT NULL, 'fedelta', 'Promozione per i clienti fedeli',
 'Il cliente dice che passerà ad approfittarne o chiede di riservare il prodotto.',
 'Presenta la promozione riservata ai clienti: cosa comprende, fino a quando vale e il prezzo o lo sconto dell''offerta. Se il cliente chiede un prodotto specifico che non è nella scheda o nell''offerta, non inventare disponibilità: dai esito operatore.',
 'Ciao {{nome}}, per i clienti di {{sede}} abbiamo una promozione riservata: vuoi che ti dica di cosa si tratta?',
 1, 8, 105, 'negozio,ristorazione,servizi'
WHERE NOT EXISTS (SELECT 1 FROM GoalModels WHERE OrganizationId IS NULL AND Code='fedelta');
GO

-- I modelli standard già presenti valgono per tutti i settori: tolte le parole da palestra
-- (l'assistente usa comunque le parole del tipo di attività).
UPDATE GoalModels SET Instructions=REPLACE(Instructions, 'non viene in palestra da tempo', 'non viene da noi da tempo')
WHERE OrganizationId IS NULL AND Code='rientro';
GO
UPDATE GoalModels SET Instructions=REPLACE(Instructions, 'quanto consiglieresti la palestra a un amico', 'quanto ci consiglieresti a un amico')
WHERE OrganizationId IS NULL AND Code='sondaggio';
GO
UPDATE GoalModels SET Sectors='palestra,benessere,formazione,ristorazione,negozio,altro'
WHERE OrganizationId IS NULL AND Code='amico' AND Sectors IS NULL;
GO
