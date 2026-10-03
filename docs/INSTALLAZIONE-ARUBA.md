# Installare mvchat su Aruba

Guida per mettere online mvchat su un hosting **Windows** di Aruba, senza usare codice.

## Cosa serve

- Un piano **Hosting Windows** di Aruba con **.NET 10** e ASP.NET Core Module V2 (verificato a ottobre 2026: presenti .NET 9 e 10).
- Un database **MySQL** (o MariaDB). Sui piani Windows Aruba non è incluso di base: va verificato che si possa aggiungere al piano, oppure va usato un database MySQL raggiungibile dal server Windows.
- Il dominio con **HTTPS** attivo (il certificato è già compreso nei piani Aruba).

## 1. Scaricare il pacchetto

1. Apri il repository **mvoglino/mvchat** su GitHub.
2. Vai nella scheda **Actions**.
3. Apri l'ultima esecuzione con la spunta verde.
4. In fondo, sotto **Artifacts**, scarica **mvchat-aruba-…**: è un file zip.
5. Estrai lo zip sul tuo computer.

La spunta verde vuol dire che GitHub ha compilato il programma e ha superato tutte le prove automatiche (installazione, accessi, separazione dei dati tra gruppi e attività, campagne, assistente, privacy).

## 2. Creare il database

Nel pannello Aruba crea un database MySQL e annota:

- indirizzo del server (host)
- nome del database
- utente
- password

## 3. Caricare i file

1. Collegati via FTP (per esempio con FileZilla) con i dati del pannello Aruba.
2. Carica **tutto il contenuto** dello zip nella cartella principale del sito (di solito `www`).
3. Crea una cartella vuota chiamata **App_Data** accanto ai file caricati.
4. Nel pannello Aruba dai alla cartella **App_Data** il **permesso di scrittura**.

App_Data è l'unica cartella dove mvchat scrive: contiene la configurazione e le chiavi degli accessi. Non va mai cancellata.

## 4. Installazione guidata

1. Apri `https://tuodominio.it/` dal browser: si apre l'installazione guidata. In quel momento mvchat crea il file **App_Data/codice-installazione.txt**.
2. Scarica quel file via FTP, aprilo e copia il codice nel campo **Codice di installazione**. Così può installare solo chi ha accesso ai file del sito; dopo l'installazione il file si cancella da solo.
3. Inserisci i dati del database del punto 2.
4. Scegli nome, email e password dell'amministratore MVitalia.
5. Premi **Installa**.

Alla fine la pagina mostra un indirizzo che finisce con `/jobs/tick?token=…`. **Conservalo**: contiene una chiave segreta.

## 5. Operazione pianificata

Nel pannello Aruba crea un'**operazione pianificata** che richiami l'indirizzo del punto 4 ogni 5 minuti, o meno se il pannello lo permette.

Serve a tenere sveglio il programma e a far ripartire il lavoro in coda:

- **campagne**: mentre mvchat è acceso invia da solo ogni 30 secondi; se Aruba lo ha spento per inattività, ogni chiamata dell'operazione pianificata fa un giro di invio (circa 25 secondi);
- **risposte dell'assistente** rimaste in sospeso dopo un riavvio.

Fa partire anche la **pulizia giornaliera dei dati** oltre il periodo di conservazione (12 mesi).

Gli orari di invio si impostano per ogni attività in *Scheda per l'assistente → Orari di invio WhatsApp* (di base: sempre).

## Aggiornare mvchat

Quando ti avviso che c'è una nuova versione:

1. Scarica il nuovo pacchetto come al punto 1.
2. Carica via FTP un file chiamato **app_offline.htm** nella cartella principale. Il sito mostra una pagina di manutenzione e libera i file in uso.
3. Carica i nuovi file sovrascrivendo i vecchi. **Non toccare la cartella App_Data.**
4. Cancella **app_offline.htm**.

Al primo avvio mvchat aggiorna da solo il database, se serve: non c'è niente da lanciare a mano. Se l'aggiornamento si interrompe (per esempio per un riavvio dell'hosting), al prossimo avvio riprende dal punto in cui si era fermato. Se non riesce, MVitalia lo vede in rosso nel pannello di controllo e su `/health`.

**Prima di ogni aggiornamento** fai una copia del database dal pannello Aruba: è il modo più semplice per tornare indietro.

## Se qualcosa non va

| Cosa vedi | Cosa fare |
|---|---|
| Errore 500.30 o 502.5 | Il piano non ha .NET 10 attivo, oppure il file App_Data/mvchat.json è rovinato (mvchat non riparte da solo per sicurezza: ripristinalo dal backup) |
| "La cartella App_Data non è scrivibile" | Ridai il permesso di scrittura ad App_Data dal pannello |
| "Non riesco a raggiungere il server del database" | Controlla host e porta del database |
| "Il database ha rifiutato utente o password" | Ricontrolla utente e password nel pannello Aruba |

Lo stato del programma si controlla anche aprendo `https://tuodominio.it/health`: risponde «ok» se il database è raggiungibile e aggiornato, altrimenti un errore (codice 503). Conviene farlo controllare ogni 5 minuti da un servizio gratuito di monitoraggio (per esempio UptimeRobot), che manda un'email se mvchat non risponde.

**Backup**: fai salvare ogni giorno il database (pannello Aruba) e tieni una copia della cartella **App_Data** (configurazione e chiavi): senza le chiavi le password dei numeri WhatsApp e dell'AI salvate cifrate non si possono più leggere.
