using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Campaigns;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Campagne;

/// <summary>
/// Numeri di prova dell'attività (facoltativi): prima di avviare una campagna si può mandare
/// il primo messaggio a questi numeri, per vedere come arriva e provare a rispondere all'assistente.
/// </summary>
public class ProvaModel : PageModel
{
    public const int Max = 10;
    private readonly CampaignRepo _repo; private readonly Repos _repos;
    public ProvaModel(CampaignRepo repo, Repos repos) { _repo = repo; _repos = repos; }

    public Gym Gym { get; private set; } = null!;
    public List<TestNumber> Items { get; private set; } = new();
    public string? Error { get; private set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public int DeleteId { get; set; }

    private async Task<bool> LoadAsync(int id)
    {
        var g = await _repos.GymAsync(User.Scope(), id); // perimetro nella query
        if (g is null) return false;
        Gym = g;
        Items = await _repo.TestNumbersAsync(id);
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAddAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        var (phone, reason) = ImportRules.NormalizePhone(Phone);
        if (string.IsNullOrWhiteSpace(Name)) Error = "Scrivi il nome di chi riceve la prova.";
        else if (phone is null) Error = "Il cellulare non è valido" + (reason is null ? "." : $": {reason}.");
        else if (Items.Count >= Max && Items.All(x => x.Phone != phone)) Error = $"Al massimo {Max} numeri di prova per attività.";
        if (Error is not null) return Page();
        var name = Name!.Trim(); if (name.Length > 100) name = name[..100];
        await _repo.AddTestNumberAsync(Gym.OrganizationId, Gym.Id, name, phone!, User.Scope().UserId);
        await _repos.AuditAsync(User.Scope(), "campaign.testnumber.added", $"{name} {phone}", HttpContext.Connection.RemoteIpAddress?.ToString(), Gym.OrganizationId, Gym.Id);
        TempData["Ok"] = "Numero di prova aggiunto.";
        return Redirect($"/Campagne/Prova/{id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (await _repo.DeleteTestNumberAsync(Gym.Id, DeleteId) > 0) TempData["Ok"] = "Numero di prova tolto.";
        return Redirect($"/Campagne/Prova/{id}");
    }
}
