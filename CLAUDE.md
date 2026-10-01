# mvchat

Piattaforma SaaS di MVitalia: campagne WhatsApp per catene di palestre, con un assistente AI che dialoga verso un obiettivo (rinnovo, promo, rientro). Primo cliente: FitActive.

## Persona e lingua
- Il committente è Maurizio (MVitalia). Non scrive codice: spiegazioni in italiano semplice, niente gergo.
- Testi dell'interfaccia, messaggi di errore e commenti nel codice in italiano.

## Decisioni prese
- .NET 8, ASP.NET Core Razor Pages, nessun ORM: ADO.NET con `Infrastructure/Db.cs` e query parametrizzate.
- Database MySQL / MariaDB tramite il pacchetto `MySqlConnector`.
- Hosting: Aruba Windows condiviso (IIS in-process). Niente processi sempre attivi: il lavoro in coda parte da `/jobs/tick?token=…`, richiamato dall'operazione pianificata di Aruba.
- Configurazione in `App_Data/mvchat.json`, scritta dall'installazione guidata (`/Install`). Mai segreti nel repository.
- Modifiche al database solo con nuovi script `Data/Schema/NNN_nome.sql` (numerati, mai modificare quelli già rilasciati). Separatore tra istruzioni: riga `GO`. Usare `IF NOT EXISTS` dove possibile: in MySQL le istruzioni DDL non sono transazionali.
- Messaggi Meta fatturati direttamente alla palestra; uso dell'AI misurato per palestra e rifatturato a consumo da MVitalia, separato dall'abbonamento.

## Separazione dei dati
- Ruoli: `superadmin` (MVitalia), `orgadmin` (direzione catena), `manager` (responsabile palestra), `operator`.
- Ogni lettura di dati di lavoro passa da `Scope` (`Security/Scope.cs`) e da `Repos`: il filtro per catena/palestra sta nella query, non nella pagina.
- Catena e palestra di un record si ricavano dalle regole lato server, mai da campi del modulo non verificati.

## Compilare e provare
- In questo ambiente NuGet può essere bloccato: `dotnet build -p:OfflineBuild=true` compila senza il driver MySQL (solo controllo sintassi).
- GitHub Actions (`.github/workflows/build.yml`) compila, avvia MariaDB, esegue `tests/e2e/e2e.py` e pubblica lo zip per Aruba come artifact.
- Ogni nuovo passo aggiunge le sue prove in `tests/e2e`.

## Piano di lavoro (10 passi)
1. Fondamenta: installazione, accessi, ruoli, catene/palestre/utenti ✅
2. Liste contatti: import Excel nel browser, consensi, normalizzazione numeri, lista STOP
3. Scheda sede e modelli di obiettivo
4. Collegamento WhatsApp Cloud API (MVitalia Tech Provider, Embedded Signup)
5. Assistente AI con esiti e passaggio all'operatore
6. Campagne con coda e limiti Meta
7. Conversazioni operatori
8. Pannello amministratore e report
9. Abbonamenti e consumi rifatturati
10. Privacy, sicurezza, sede pilota
