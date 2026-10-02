# Avvio dell'attività pilota (FitActive)

Percorso consigliato per mettere in funzione mvchat con la prima palestra FitActive, dal server alle prime campagne vere. Ogni passo si fa dalle pagine di mvchat, senza codice.

## 1. Server (MVitalia)

1. Installa mvchat su Aruba seguendo [INSTALLAZIONE-ARUBA.md](INSTALLAZIONE-ARUBA.md), con HTTPS attivo.
2. Crea l'**operazione pianificata** su `/jobs/tick?token=…` ogni 5 minuti.
3. Apri il **Pannello**: nella sezione «Da controllare» non deve restare niente di urgente.

## 2. Impostazioni generali (MVitalia)

| Pagina | Cosa inserire |
|---|---|
| Impostazioni Meta | App ID e chiave segreta dell'app Meta di MVitalia; copia su Meta l'indirizzo del webhook e la parola d'ordine mostrati |
| Impostazioni AI | Fornitore (Anthropic o OpenAI), chiave, prova del collegamento |
| Fatturazione → Impostazioni | Canone di base, ricarico AI (20%), cambio dollaro/euro, dati di MVitalia |
| Privacy e dati | Conservazione 12 mesi (già impostata) |

## 3. Gruppo e attività pilota

1. **Gruppi → Abilita un gruppo**: FitActive, tipo «Palestra / centro fitness».
2. **Dati e logo** del gruppo: ragione sociale, P.IVA, logo, «Chi siamo», **link all'informativa privacy**.
3. **Attività → Nuova attività**, nel gruppo FitActive: la palestra pilota, con data di inizio dell'abbonamento.
4. **Utenti**: amministratore di gruppo (direzione FitActive), amministratore dell'attività, 1–2 operatori della reception. Ognuno riceve una password provvisoria da cambiare al primo accesso.

## 4. WhatsApp della palestra

1. Segui la guida «Account Meta e numero WhatsApp per mvchat».
2. In **WhatsApp → numero della palestra** scegli «Meta» e inserisci Phone number ID, WABA ID e chiave. Premi «Controlla collegamento»: lo stato deve diventare **attivo** (solo da quel momento mvchat accetta i messaggi in arrivo su quel numero).
3. Crea il **template** del primo messaggio e aspetta l'approvazione di Meta (di solito da pochi minuti a 24 ore).

## 5. Contenuti per l'assistente (amministratore attività)

1. **Scheda per l'assistente**: orari, servizi, corsi, come arrivare, domande frequenti. Più è completa, meno conversazioni passano alla reception.
2. **Offerte**: la promo con prezzo, eventuale prezzo pieno e sconto massimo concesso.
3. **Orari di invio** (di base sempre): per esempio lun–sab 9–20.
4. **Risposte rapide** per la reception.

## 6. Prove prima dei clienti

1. **Prova l'assistente** con il proprio cellulare: rispondere come farebbe un cliente, chiedere il prezzo, chiedere una persona, scrivere STOP.
2. **Banco di prova**: eseguire tutti gli scenari; correggere scheda o offerta se qualcosa è «da rivedere».
3. Controllare in **Conversazioni** che gli operatori ricevano l'avviso e sappiano prendere in carico.

## 7. Prima campagna vera

1. **Liste contatti**: caricare il file Excel con **solo chi ha dato il consenso** (mvchat scarta gli altri e lo dice).
2. **Campagne → Nuova**: partire piccoli, per esempio **limite di 20–30 invii al giorno** per la prima settimana. Meta parte da 250 persone nuove al giorno per numero e controlla la qualità: invii graduali proteggono il numero.
3. Seguire per 2–3 giorni **Pannello** e **Report**: risposte, obiettivi raggiunti, conversazioni passate alla reception, qualità del numero.
4. Se tutto va bene, alzare il limite e passare alle altre palestre.

## 8. Privacy: cosa deve essere a posto

Da verificare con il consulente privacy di MVitalia e di FitActive:

- **Ruoli**: ogni attività (o il gruppo) è *titolare* dei dati dei suoi clienti; MVitalia è *responsabile del trattamento* e serve un **accordo di nomina** (art. 28 GDPR).
- **Sub-responsabili** da indicare nell'accordo: Aruba (server e database), Meta (WhatsApp), il fornitore AI scelto (Anthropic o OpenAI, con il loro accordo sul trattamento dei dati).
- **Informativa** dell'attività: deve citare l'invio di messaggi WhatsApp promozionali e l'uso di un **assistente virtuale** (AI Act: l'assistente si presenta sempre come tale, mvchat lo garantisce).
- **Consenso marketing** nella lista: si caricano solo contatti con consenso; chi scrive STOP o chiede di non essere contattato entra subito nella lista STOP.
- **Richieste dei clienti** (vedere o cancellare i propri dati): menu *Richieste privacy*, cerca il cellulare, scarica i dati o cancellali.
- **Conservazione**: 12 mesi, poi la cancellazione è automatica.

## Contatti in caso di problemi

- Pannello «Da controllare» → ogni avviso porta alla pagina da sistemare.
- Campagna in pausa da sola → la pagina della campagna spiega il motivo (chiave Meta, qualità del numero, template, offerta scaduta).
