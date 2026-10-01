// mvchat · caricamento lista contatti.
// Il file Excel viene letto qui nel browser: al server arrivano solo le righe e l'abbinamento delle colonne.
(function () {
  "use strict";
  const FIELDS = [
    { key: "FirstName", label: "Nome", required: true, words: ["nome", "first name", "firstname", "name"] },
    { key: "LastName", label: "Cognome", words: ["cognome", "last name", "lastname", "surname"] },
    { key: "Phone", label: "Cellulare / WhatsApp", required: true, words: ["cellulare", "whatsapp", "cell", "mobile", "telefono", "tel", "phone", "numero"] },
    { key: "Email", label: "Email", words: ["email", "e-mail", "mail"] },
    { key: "Membership", label: "Abbonamento", words: ["abbonamento", "tipo abbonamento", "piano", "formula", "membership", "contratto"] },
    { key: "ExpiresOn", label: "Scadenza abbonamento", words: ["scadenza", "data scadenza", "scadenza abbonamento", "fine abbonamento", "expiry", "valido fino"] },
    { key: "Consent", label: "Consenso marketing", required: true, words: ["consenso marketing", "marketing", "consenso whatsapp", "consenso commerciale", "consenso comunicazioni", "newsletter", "consenso"] },
    { key: "ConsentDate", label: "Data consenso", words: ["data consenso", "consenso data", "data privacy"] },
    { key: "ConsentSource", label: "Fonte consenso", words: ["fonte consenso", "origine consenso", "fonte", "canale"] },
  ];
  const $ = (s) => document.querySelector(s);
  const saved = JSON.parse($("#mappings").textContent || "{}");
  const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
  let header = [], rows = [], fileName = "";

  const norm = (s) => String(s ?? "").toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]+/g, " ").trim();
  const cellText = (v) => {
    if (v instanceof Date) {
      const d = new Date(v.getTime() - v.getTimezoneOffset() * 60000);
      return d.toISOString().slice(0, 10);
    }
    if (typeof v === "number") return Number.isInteger(v) ? String(v) : String(v);
    return String(v ?? "").trim();
  };

  function readFile(file) {
    fileName = file.name;
    if (!$("#listName").value) $("#listName").value = file.name.replace(/\.(xlsx|xls|csv)$/i, "").replace(/[_-]+/g, " ");
    const reader = new FileReader();
    reader.onload = (e) => {
      try {
        const wb = XLSX.read(new Uint8Array(e.target.result), { type: "array", cellDates: true });
        const ws = wb.Sheets[wb.SheetNames[0]];
        const all = XLSX.utils.sheet_to_json(ws, { header: 1, raw: true, defval: "" }).map((r) => r.map(cellText));
        const h = all.findIndex((r) => r.filter((c) => c !== "").length >= 2);
        if (h < 0) { $("#fileInfo").textContent = "Il file sembra vuoto."; return; }
        header = all[h];
        rows = all.slice(h + 1).filter((r) => r.some((c) => c !== ""));
        $("#fileInfo").textContent = `${file.name}: ${rows.length} righe, ${header.length} colonne (foglio «${wb.SheetNames[0]}»).`;
        buildMapping();
      } catch (err) {
        $("#fileInfo").textContent = "Non riesco a leggere il file. Salvalo come .xlsx e riprova.";
      }
    };
    reader.readAsArrayBuffer(file);
  }

  function guess(field) {
    const gym = $("#gym").value;
    const prev = saved[gym];
    if (prev && prev.Headers && prev.Headers[field.key]) {
      const i = header.findIndex((h) => norm(h) === norm(prev.Headers[field.key]));
      if (i >= 0) return i;
    }
    const hs = header.map(norm);
    for (const w of field.words) { const i = hs.indexOf(w); if (i >= 0) return i; }
    for (const w of field.words) { const i = hs.findIndex((h) => h.includes(w)); if (i >= 0) return i; }
    return -1;
  }

  function buildMapping() {
    const taken = new Set();
    $("#mapFields").innerHTML = "";
    for (const f of FIELDS) {
      let idx = guess(f);
      if (taken.has(idx)) idx = -1;
      if (idx >= 0) taken.add(idx);
      const wrap = document.createElement("div");
      wrap.className = "field";
      const id = "map_" + f.key;
      wrap.innerHTML = `<label for="${id}">${f.label}${f.required ? " *" : ""}</label>`;
      const sel = document.createElement("select");
      sel.id = id; sel.dataset.key = f.key;
      sel.add(new Option(f.required ? "— scegli la colonna —" : "— non presente —", "-1"));
      header.forEach((h, i) => sel.add(new Option(h || `Colonna ${i + 1}`, String(i))));
      sel.value = String(idx);
      sel.addEventListener("change", renderPreview);
      wrap.appendChild(sel);
      $("#mapFields").appendChild(wrap);
    }
    $("#mapPanel").hidden = false;
    renderPreview();
  }

  function mapping() {
    const m = { Headers: {} };
    document.querySelectorAll("#mapFields select").forEach((s) => {
      const i = parseInt(s.value, 10);
      m[s.dataset.key] = i;
      if (i >= 0) m.Headers[s.dataset.key] = header[i];
    });
    return m;
  }

  function renderPreview() {
    const m = mapping();
    const used = FIELDS.filter((f) => m[f.key] >= 0);
    const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
    $("#preview").innerHTML =
      "<thead><tr>" + used.map((f) => `<th>${f.label}</th>`).join("") + "</tr></thead><tbody>" +
      rows.slice(0, 5).map((r) => "<tr>" + used.map((f) => `<td>${esc(r[m[f.key]] ?? "")}</td>`).join("") + "</tr>").join("") + "</tbody>";
    const missing = FIELDS.filter((f) => f.required && m[f.key] < 0).map((f) => f.label);
    $("#importBtn").disabled = missing.length > 0;
    $("#status").textContent = missing.length ? "Manca: " + missing.join(", ") : `${rows.length} righe pronte da controllare`;
  }

  async function doImport() {
    const btn = $("#importBtn");
    btn.disabled = true;
    $("#status").textContent = "Controllo e importazione in corso…";
    const body = {
      GymId: parseInt($("#gym").value, 10),
      Name: $("#listName").value.trim(),
      FileName: fileName,
      Map: mapping(),
      Rows: rows,
    };
    try {
      const res = await fetch("?handler=Import", {
        method: "POST",
        headers: { "Content-Type": "application/json", RequestVerificationToken: token },
        body: JSON.stringify(body),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) { $("#status").textContent = data.error || "Importazione non riuscita. Riprova."; btn.disabled = false; return; }
      location.href = "/Lists/Detail/" + data.listId + "?nuova=1";
    } catch {
      $("#status").textContent = "Connessione interrotta. Riprova.";
      btn.disabled = false;
    }
  }

  function downloadTemplate() {
    const cols = ["Nome", "Cognome", "Cellulare", "Email", "Tipo abbonamento", "Scadenza abbonamento", "Consenso marketing", "Data consenso", "Fonte consenso"];
    const ws = XLSX.utils.aoa_to_sheet([cols, ["Giulia", "Rossi", "333 123 4567", "giulia@esempio.it", "Annuale", "18/10/2026", "SI", "12/10/2025", "Modulo iscrizione"]]);
    ws["!cols"] = cols.map(() => ({ wch: 20 }));
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, "Contatti");
    XLSX.writeFile(wb, "modello-lista-mvchat.xlsx");
  }

  const drop = $("#drop"), file = $("#file");
  file.addEventListener("change", () => file.files[0] && readFile(file.files[0]));
  drop.addEventListener("dragover", (e) => { e.preventDefault(); drop.classList.add("over"); });
  drop.addEventListener("dragleave", () => drop.classList.remove("over"));
  drop.addEventListener("drop", (e) => { e.preventDefault(); drop.classList.remove("over"); e.dataTransfer.files[0] && readFile(e.dataTransfer.files[0]); });
  $("#gym").addEventListener("change", () => header.length && buildMapping());
  $("#importBtn").addEventListener("click", doImport);
  $("#tplBtn").addEventListener("click", downloadTemplate);
})();
