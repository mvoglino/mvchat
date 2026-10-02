# mvchat

Piattaforma SaaS di MVitalia: campagne WhatsApp per strutture di qualsiasi settore (palestre, hotel, centri benessere, studi, negozi…), ciascuna con più sedi, con un assistente AI che dialoga verso un obiettivo (rinnovo, promo, rientro). Primo cliente: FitActive (palestre).

## Persona e lingua
- Il committente è Maurizio (MVitalia). Non scrive codice: spiegazioni in italiano semplice, niente gergo.
- Testi dell'interfaccia, messaggi di errore e commenti nel codice in italiano.

## Decisioni prese
- .NET 10 (LTS, supporto fino a novembre 2028), ASP.NET Core Razor Pages, nessun ORM: ADO.NET con `Infrastructure/Db.cs` e query parametrizzate.
- Database MySQL / MariaDB tramite il pacchetto `MySqlConnector`.
- Hosting: Aruba Windows condiviso (IIS in-process). Sul server Aruba (verificato ottobre 2026): .NET 9.0.20 e 10.0.12, ASP.NET Core Module V2: niente .NET 8. Niente processi sempre attivi: il lavoro in coda parte da `/jobs/tick?token=…`, richiamato dall'operazione pianificata di Aruba.
- Configurazione in `App_Data/mvchat.json`, scritta dall'installazione guidata (`/Install`). Mai segreti nel repository.
- Modifiche al database solo con nuovi script `Data/Schema/NNN_nome.sql` (numerati, mai modificare quelli già rilasciati). Separatore tra istruzioni: riga `GO`. Usare `IF NOT EXISTS` dove possibile: in MySQL le istruzioni DDL non sono transazionali.
- Prezzi solo nelle offerte, definiti da ogni palestra: la scheda sede non contiene prezzi e l'assistente può citare solo il prezzo dell'offerta collegata.
- Nessun collegamento al gestionale delle palestre: l'esito di una campagna è quello rilevato nella conversazione.
- Date e ore salvate in UTC (la connessione imposta time_zone='+00:00'); in pagina si mostrano con `.ToRome()`.
- Messaggi Meta fatturati direttamente alla palestra; uso dell'AI misurato per palestra e rifatturato a consumo da MVitalia, separato dall'abbonamento.

## Parole e settori
- Nell'interfaccia: **struttura** (il cliente abilitato da MVitalia; nel codice `Organization`), **sede** (nel codice `Gym`, tabella `Gyms`), **cliente** (il destinatario). Mai «palestra» o «catena» nei testi delle pagine.
- Ogni struttura ha un tipo di attività (`Infrastructure/Sectors.cs`): cambia solo le parole che l'assistente usa con i clienti (iscritto/ospite/paziente, abbonamento/soggiorno…) e l'etichetta delle attività nella scheda sede.
- Nome, colore, logo (caricato nel database, servito da `/logo/{id}`) e dati dell'attività in *Dati e logo* (`/Struttura`): MVitalia per tutte, la direzione per la propria; il tipo di attività lo cambia solo MVitalia. La presentazione «Chi siamo» entra nelle istruzioni dell'AI.
- Segnaposto dei template: `{{sede}}`; `{{palestra}}` resta valido come sinonimo per i template già approvati.

## Separazione dei dati
- Ruoli: `superadmin` (MVitalia), `orgadmin` (direzione della struttura), `manager` (responsabile di sede), `operator`.
- Ogni lettura di dati di lavoro passa da `Scope` (`Security/Scope.cs`) e da `Repos`: il filtro per struttura/sede sta nella query, non nella pagina.
- Struttura e sede di un record si ricavano dalle regole lato server, mai da campi del modulo non verificati.

## Compilare e provare
- In questo ambiente NuGet può essere bloccato e c'è solo l'SDK .NET 8: `dotnet build -p:OfflineBuild=true -p:TargetFramework=net8.0` compila senza il driver MySQL (solo controllo sintassi). La build vera su .NET 10 la fa GitHub Actions.
- GitHub Actions (`.github/workflows/build.yml`) compila, avvia MariaDB, esegue `tests/e2e/e2e.py` e pubblica lo zip per Aruba come artifact.
- Ogni nuovo passo aggiunge le sue prove in `tests/e2e`.

## Piano di lavoro (10 passi)
1. Fondamenta: installazione, accessi, ruoli, catene/palestre/utenti ✅
2. Liste contatti: import Excel nel browser con abbinamento colonne ricordato per palestra, consensi, normalizzazione numeri, lista STOP per catena ✅
3. Scheda sede (senza prezzi), offerte con prezzo per palestra, modelli di obiettivo (standard MVitalia + della catena), anteprima istruzioni AI (`Catalog/PromptBuilder.cs`) ✅
4. Collegamento WhatsApp Cloud API: numeri per palestra simulati o Meta (inserimento manuale ID + chiave cifrata), template con approvazione, invio, webhook firmato, STOP automatico, finto server Meta nelle prove ✅ · da fare quando MVitalia sarà Tech Provider: Embedded Signup
5. Assistente AI (Anthropic o OpenAI, scelto in Impostazioni AI, chiave cifrata): risposte in JSON {risposta, esito, nota}, controlli di mvchat prima dell'invio (prezzi solo dall'offerta e sconto massimo, dichiararsi assistente virtuale, limite risposte), passaggio alla reception, coda in memoria + ripresa da /jobs/tick, pagina Conversazioni, banco di prova con clienti simulati, consumi in AiUsage; finto fornitore AI nelle prove (`tests/e2e/fake_ai.py`) ✅
6. Campagne (`Campaigns/`): destinatari copiati dalla lista alla creazione, bozza → programmata/in invio → completata, pausa/ripresa/annullamento; invio a gruppi con prenotazione dei destinatari (mai due volte), ogni 30 s (CampaignWorker) e da /jobs/tick; orari di invio per palestra in `GymSendWindows` (nessuna riga = 24 ore su 24, ora italiana); limite giornaliero della campagna e limite Meta del numero (persone nuove in 24 ore dal tier); pausa automatica per errori di chiave/account Meta, qualità RED, template non approvato, offerta non attiva; salta chi è in lista STOP o ha già una conversazione aperta ✅
7. Postazione reception (avvisi solo dentro mvchat, niente email): viste da gestire/mie/assistente/chiuse/tutte, presa in carico, lascia ai colleghi, assegnazione dal responsabile, avviso nel menu e nel titolo con controllo ogni 30 s (`/Conversazioni?handler=Badge`); risposte rapide per palestra o catena (`QuickReplies`, {{nome}} {{palestra}}); archivio dialoghi diviso «solo assistente AI» / «con operatore» (`Conversations.HumanInvolved`, impostato quando la conversazione passa alla reception o un operatore scrive) con filtri, numeri e file per Excel ✅
8. Pannello amministratore e report
9. Abbonamenti e consumi rifatturati
10. Privacy, sicurezza, sede pilota
