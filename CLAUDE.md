# mvchat

Piattaforma SaaS erogata da MVitalia: campagne WhatsApp per attività di qualsiasi settore (palestre, hotel, centri benessere, studi, negozi…), singole o riunite in gruppi (marchi), con un assistente AI che dialoga verso un obiettivo (rinnovo, promo, rientro). Primo cliente: FitActive (palestre).

## Persona e lingua
- Il committente è Maurizio (MVitalia). Non scrive codice: spiegazioni in italiano semplice, niente gergo.
- Testi dell'interfaccia, messaggi di errore e commenti nel codice in italiano.

## Decisioni prese
- .NET 10 (LTS, supporto fino a novembre 2028), ASP.NET Core Razor Pages, nessun ORM: ADO.NET con `Infrastructure/Db.cs` e query parametrizzate.
- Database MySQL / MariaDB tramite il pacchetto `MySqlConnector`.
- Hosting: Aruba Windows condiviso (IIS in-process). Sul server Aruba (verificato ottobre 2026): .NET 9.0.20 e 10.0.12, ASP.NET Core Module V2: niente .NET 8. Niente processi sempre attivi: il lavoro in coda parte da `/jobs/tick?token=…`, richiamato dall'operazione pianificata di Aruba.
- Configurazione in `App_Data/mvchat.json`, scritta dall'installazione guidata (`/Install`). Mai segreti nel repository.
- Modifiche al database solo con nuovi script `Data/Schema/NNN_nome.sql` (numerati, mai modificare quelli già rilasciati). Separatore tra istruzioni: riga `GO`. Usare `IF NOT EXISTS` dove possibile: in MySQL le istruzioni DDL non sono transazionali.
- Prezzi solo nelle offerte, definiti da ogni sede: la scheda sede non contiene prezzi e l'assistente può citare solo il prezzo dell'offerta collegata.
- Nessun collegamento al gestionale delle strutture: l'esito di una campagna è quello rilevato nella conversazione.
- Date e ore salvate in UTC (la connessione imposta time_zone='+00:00'); in pagina si mostrano con `.ToRome()`.
- Messaggi Meta fatturati direttamente alla struttura; uso dell'AI misurato per sede e rifatturato a consumo da MVitalia, separato dall'abbonamento.

## Gerarchia del servizio e parole
- **MVitalia** eroga il servizio: amministratore generale (`superadmin`) con tutti i poteri e il controllo su tutto.
- **Attività** (nel codice `Gym`, tabella `Gyms`): la singola palestra, hotel, studio… Ha il suo **amministratore attività** (`manager`) e i suoi **operatori** (`operator`).
- **Gruppo / marchio** (nel codice `Organization`, `IsGroup = 1`): più attività collegate (es. FitActive) con un **amministratore di gruppo** (`orgadmin`) che vede tutte le sue attività.
- **Attività singola**: ha comunque un contenitore in `Organizations` con `IsGroup = 0` e lo stesso nome (nome e stato sincronizzati); non compare tra i gruppi e non può avere un amministratore di gruppo. Si crea da *Attività → Nuova attività* scegliendo «attività singola».
- Nelle pagine si dice «gruppo», «attività», «cliente»: mai «palestra», «catena», «sede» o «struttura».
- Tipo di attività (`Infrastructure/Sectors.cs`): del gruppo, ogni attività può averne uno proprio (`Gyms.Sector`, vuoto = come il gruppo); cambia solo le parole dell'assistente con i clienti (iscritto/ospite/paziente…). Lo cambiano MVitalia e, dentro un gruppo, l'amministratore del gruppo.
- Nome, colore, logo e dati (ragione sociale, P.IVA, contatti, «Chi siamo»): del gruppo in `/Gruppo` (logo `/logo/{id}`), dell'attività in `/Attivita` (logo `/logo/a/{id}`). Chi lavora in un'attività vede logo e colore dell'attività, altrimenti quelli del gruppo. Nelle istruzioni dell'AI entrano «Chi siamo» dell'attività e del gruppo.
- Segnaposto dei template: `{{sede}}` (nome dell'attività); `{{palestra}}` resta valido come sinonimo.

## Separazione dei dati
- Ruoli: `superadmin` (MVitalia), `orgadmin` (amministratore di gruppo), `manager` (amministratore attività), `operator` (operatore).
- Ogni lettura di dati di lavoro passa da `Scope` (`Security/Scope.cs`) e da `Repos`: il filtro per gruppo/attività sta nella query, non nella pagina.
- Gruppo e attività di un record si ricavano dalle regole lato server, mai da campi del modulo non verificati.

## Compilare e provare
- In questo ambiente NuGet può essere bloccato e c'è solo l'SDK .NET 8: `dotnet build -p:OfflineBuild=true -p:TargetFramework=net8.0` compila senza il driver MySQL (solo controllo sintassi). La build vera su .NET 10 la fa GitHub Actions.
- GitHub Actions (`.github/workflows/build.yml`) compila, avvia MariaDB, esegue `tests/e2e/e2e.py` e pubblica lo zip per Aruba come artifact.
- Ogni nuovo passo aggiunge le sue prove in `tests/e2e`.

## Piano di lavoro (10 passi)
1. Fondamenta: installazione, accessi, ruoli, strutture/sedi/utenti ✅
2. Liste contatti: import Excel nel browser con abbinamento colonne ricordato per sede, consensi, normalizzazione numeri, lista STOP per struttura ✅
3. Scheda sede (senza prezzi), offerte con prezzo per sede, modelli di obiettivo (standard MVitalia + della struttura), anteprima istruzioni AI (`Catalog/PromptBuilder.cs`) ✅
4. Collegamento WhatsApp Cloud API: numeri per sede simulati o Meta (inserimento manuale ID + chiave cifrata), template con approvazione, invio, webhook firmato, STOP automatico, finto server Meta nelle prove ✅ · da fare quando MVitalia sarà Tech Provider: Embedded Signup
5. Assistente AI (Anthropic o OpenAI, scelto in Impostazioni AI, chiave cifrata): risposte in JSON {risposta, esito, nota}, controlli di mvchat prima dell'invio (prezzi solo dall'offerta e sconto massimo, dichiararsi assistente virtuale, limite risposte), passaggio alla reception, coda in memoria + ripresa da /jobs/tick, pagina Conversazioni, banco di prova con clienti simulati, consumi in AiUsage; finto fornitore AI nelle prove (`tests/e2e/fake_ai.py`) ✅
6. Campagne (`Campaigns/`): destinatari copiati dalla lista alla creazione, bozza → programmata/in invio → completata, pausa/ripresa/annullamento; invio a gruppi con prenotazione dei destinatari (mai due volte), ogni 30 s (CampaignWorker) e da /jobs/tick; orari di invio per palestra in `GymSendWindows` (nessuna riga = 24 ore su 24, ora italiana); limite giornaliero della campagna e limite Meta del numero (persone nuove in 24 ore dal tier); pausa automatica per errori di chiave/account Meta, qualità RED, template non approvato, offerta non attiva; salta chi è in lista STOP o ha già una conversazione aperta ✅
7. Postazione reception (avvisi solo dentro mvchat, niente email): viste da gestire/mie/assistente/chiuse/tutte, presa in carico, lascia ai colleghi, assegnazione dal responsabile, avviso nel menu e nel titolo con controllo ogni 30 s (`/Conversazioni?handler=Badge`); risposte rapide per palestra o catena (`QuickReplies`, {{nome}} {{palestra}}); archivio dialoghi diviso «solo assistente AI» / «con operatore» (`Conversations.HumanInvolved`, impostato quando la conversazione passa alla reception o un operatore scrive) con filtri, numeri e file per Excel ✅
8. Pannello amministratore e report
9. Abbonamenti e consumi rifatturati
10. Privacy, sicurezza, sede pilota
