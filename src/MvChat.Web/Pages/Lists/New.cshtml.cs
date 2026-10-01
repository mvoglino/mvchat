using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Lists;

[RequestSizeLimit(30_000_000)]
public class NewModel : PageModel
{
    private readonly ContactsRepo _contacts;
    private readonly Repos _repos;
    public NewModel(ContactsRepo contacts, Repos repos) { _contacts = contacts; _repos = repos; }

    public List<Gym> Gyms { get; private set; } = new();
    public string MappingsJson { get; private set; } = "{}";

    public async Task OnGetAsync()
    {
        Gyms = (await _repos.GymsAsync(User.Scope())).Where(g => g.IsActive).ToList();
        var maps = new Dictionary<int, ColumnMap>();
        foreach (var g in Gyms) if (await _contacts.MappingAsync(g.Id) is { } m) maps[g.Id] = m;
        MappingsJson = JsonSerializer.Serialize(maps);
    }

    public async Task<IActionResult> OnPostImportAsync([FromBody] ImportRequest req)
    {
        var me = User.Scope();
        var gym = await _repos.GymAsync(me, req.GymId);
        if (gym is null || !gym.IsActive) return Problem("Scegli una palestra valida.");
        if (string.IsNullOrWhiteSpace(req.Name)) return Problem("Dai un nome alla lista.");
        if (req.Map.Phone < 0 || req.Map.FirstName < 0 || req.Map.Consent < 0)
            return Problem("Indica quali colonne contengono nome, cellulare e consenso.");
        if (req.Rows.Count == 0) return Problem("Il file non contiene righe.");
        if (req.Rows.Count > ContactsRepo.MaxRows) return Problem($"Il file ha più di {ContactsRepo.MaxRows:N0} righe: dividilo in più liste.");

        var result = await _contacts.ImportAsync(me, gym.OrganizationId, req);
        await _repos.AuditAsync(me, "list.imported", $"{req.Name} · {result.Valid}/{result.RowsRead} contattabili",
            HttpContext.Connection.RemoteIpAddress?.ToString(), gym.OrganizationId, gym.Id);
        return new JsonResult(result);
    }

    private IActionResult Problem(string message) => new JsonResult(new { error = message }) { StatusCode = 400 };
}
