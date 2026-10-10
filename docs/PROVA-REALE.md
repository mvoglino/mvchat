# Prova reale di mvchat con WhatsApp (passo per passo)

Obiettivo: mandare una vera campagna WhatsApp ai propri telefoni, rispondere e vedere l'assistente e la reception al lavoro. Prima con il **numero di prova gratuito di Meta**, poi con il numero vero dell'attività.

Già fatto: mvchat online su https://mvchat.mvitalia.com, operazione pianificata attiva, Anthropic collegato.

## Parte A · Meta (una volta sola, da MVitalia)

1. **Business Manager**: su business.facebook.com crea (o usa) il portfolio aziendale di MVitalia. La verifica dell'azienda (visura, documenti) si può avviare subito: serve per superare i limiti iniziali di invio, non per la prova.
2. **App**: su developers.facebook.com → Le mie app → Crea app → tipo «Business» (o «Altro» → «Business»), collegata al portfolio MVitalia.
3. Nell'app aggiungi il prodotto **WhatsApp** → «Configurazione API». Meta crea da solo:
   - un **numero di prova** gratuito,
   - un **WhatsApp Business Account** di prova.
   Annota dalla pagina: **ID numero di telefono** e **ID account WhatsApp Business**.
4. Sempre in «Configurazione API», nel campo «A», aggiungi e verifica con il codice SMS **i tuoi cellulari di prova** (massimo 5): il numero di prova di Meta scrive solo a loro.
5. **Chiave di accesso permanente** (quella temporanea della pagina scade in 24 ore):
   business.facebook.com → Impostazioni → Utenti di sistema → Aggiungi (ruolo Amministratore) → «Assegna risorse»: l'app (controllo completo) e l'account WhatsApp (controllo completo) → «Genera token»: scegli l'app, scadenza «Mai», permessi **whatsapp_business_messaging** e **whatsapp_business_management**. Copia il token: si vede una volta sola.
6. **Chiave segreta dell'app**: developers.facebook.com → la tua app → Impostazioni dell'app → Di base → «Chiave segreta» (Mostra). Annota anche l'**ID app**. Nella stessa pagina inserisci l'URL dell'informativa privacy di MVitalia (serve per pubblicare l'app).

## Parte B · mvchat: collegare Meta (MVitalia)

7. mvchat → MVitalia → **Impostazioni Meta**: inserisci ID app, Chiave segreta, versione API (lascia quella proposta) e salva. In alto compaiono **URL di callback** e **Token di verifica**.
8. developers.facebook.com → la tua app → WhatsApp → **Configurazione → Webhook** → Modifica: incolla URL di callback e Token di verifica → «Verifica e salva». Poi in «Campi webhook» attiva: **messages**, **message_template_status_update**, **phone_number_quality_update**, **account_update**.
9. Nell'app metti la modalità **Live / Pubblicata** (interruttore in alto o «Pubblica»): senza, Meta non manda i messaggi dei clienti a mvchat.

## Parte C · mvchat: l'attività pilota

10. **Organizzazione → Attività**: controlla che esista l'attività di prova (es. FitActive Alba) nel gruppo FitActive, attiva.
11. **Organizzazione → WhatsApp: numero e template** → numero dell'attività → modalità **Meta**: numero (quello di prova di Meta), nome, ID numero di telefono, ID account WhatsApp Business, chiave di accesso del punto 5 → Salva. mvchat lo controlla con Meta: deve comparire la qualità (GREEN) e il limite di invio.
12. **Template** (stessa pagina): crea il primo messaggio, categoria **MARKETING**, per esempio:
    «Ciao {{nome}}, il tuo abbonamento {{abbonamento}} in {{sede}} scade il {{scadenza}}. Ti scrivo per un'offerta dedicata: vuoi saperne di più?»
    Meta lo approva di solito in pochi minuti (a volte fino a 24 ore): premi «Aggiorna stato» finché è **approvato**. Se vuoi provare anche il promemoria, crea un secondo template breve.
13. **Assistente AI → 1 Scheda**: orari, servizi, corsi, come arrivare, domande frequenti, link di prenotazione (facoltativo). Niente prezzi.
14. **Assistente AI → 2 Offerte**: l'offerta con prezzo, prezzo pieno, sconto extra massimo, validità, link di pagamento (facoltativo).
15. **Assistente AI → 3 Modelli**: scegli il modello di obiettivo (es. Rinnovo), controlla «messaggi massimi».
16. **Assistente AI → 4 Anteprima**: leggi le istruzioni complete che riceverà l'AI.
17. **Assistente AI → 6 Banco di prova**: lancia tutti gli scenari; devono uscire verdi o con avvisi gialli.

## Parte D · la prima campagna vera (sui propri telefoni)

18. **Campagne → numeri di prova** (`/Campagne/Prova/…`): aggiungi i tuoi cellulari (gli stessi del punto 4).
19. **Campagne → 1 Liste**: importa un Excel con 2–3 righe: i tuoi cellulari, nome, tipo abbonamento, scadenza, consenso «SI».
20. **Campagne → 2 Campagne → Nuova**: lista, obiettivo, offerta, template approvato, «subito». Promemoria e domanda sul motivo a piacere.
21. Nella campagna premi **«Invia una prova»**: arriva il primo messaggio sui telefoni di prova senza avviare la campagna. Rispondi dal telefono e guarda **Reception → Conversazioni**: in pochi secondi risponde l'assistente.
22. Prova le situazioni tipiche dal telefono:
    - «quanto costa?» → prezzo dell'offerta;
    - «posso parlare con qualcuno?» → messaggio di cortesia e conversazione in reception (avviso nel menù);
    - dalla reception: «Prendi in carico», rispondi, «Restituisci all'assistente»;
    - «no grazie» → domanda sul motivo, poi il Report → Motivi dei rifiuti;
    - «STOP» (da un numero solo) → conferma e lista STOP.
23. Se tutto va bene, **avvia** la campagna: parte verso tutta la lista (solo i tuoi numeri).
24. Controlla **Risultati → Report** e la pagina della campagna.

## Parte E · dopo la prova: il numero vero dell'attività

25. Serve un numero **non usato sull'app WhatsApp** (oppure da cancellare dall'app prima). In WhatsApp Manager (business.facebook.com → WhatsApp Manager → Numeri di telefono) aggiungi il numero, verifica con SMS/chiamata, imposta il nome visualizzato (Meta lo approva).
26. In WhatsApp Manager aggiungi un **metodo di pagamento**: i messaggi marketing sono a pagamento (Meta fattura direttamente).
27. In mvchat cambia il numero dell'attività con i dati del numero vero (nuovo ID numero di telefono, ID account WhatsApp Business se diverso) e ricrea/ri-approva i template su quell'account.
28. Prima campagna piccola (20–50 clienti con consenso), poi si cresce: i limiti di Meta salgono da soli se la qualità resta buona.

## Se qualcosa non va

| Cosa vedi | Cosa controllare |
|---|---|
| Il numero non si salva in mvchat | ID numero di telefono e chiave di accesso (punto 5, permessi giusti) |
| Il template resta «in revisione» | Aspetta e premi «Aggiorna stato»; se rifiutato, il motivo è nella pagina |
| Il messaggio parte ma la risposta del cliente non arriva in mvchat | Webhook (punto 8): campo «messages» attivo, app Live (punto 9), chiave segreta giusta (punto 6) |
| Errore «numero non autorizzato» con il numero di prova | Il destinatario non è tra i numeri verificati del punto 4 |
| L'assistente non risponde | Impostazioni AI → Prova il collegamento; Registro tecnico |
