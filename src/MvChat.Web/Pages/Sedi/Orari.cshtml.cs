using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Campaigns;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Sedi;

/// <summary>Orari in cui la sede permette l'invio dei primi messaggi delle campagne. Di base: sempre.</summary>
public class OrariModel : PageModel
{
    private readonly Repos _repos; private readonly CampaignRepo _campaigns;
    public OrariModel(Repos repos, CampaignRepo campaigns) { _repos = repos; _campaigns = campaigns; }

    public Gym Gym { get; private set; } = null!;
    public string? Error { get; private set; }
    public string Current { get; private set; } = "";

    [BindProperty] public string Mode { get; set; } = "sempre";
    [BindProperty] public List<DayInput> Days { get; set; } = new();

    public class DayInput
    {
        public bool On { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
    }

    private async Task<bool> LoadAsync(int id)
    {
        var g = await _repos.GymAsync(User.Scope(), id);
        if (g is null) return false;
        Gym = g;
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        var w = await _campaigns.WindowsAsync(id);
        Current = SendWindows.Describe(w);
        Mode = w.Count == 0 ? "sempre" : "fasce";
        Days = Enumerable.Range(1, 7).Select(d =>
        {
            var x = w.FirstOrDefault(v => v.Day == d);
            // Proposta iniziale quando si passa alle fasce: lun–sab 9–20, domenica chiuso.
            return x is null
                ? new DayInput { On = w.Count == 0 && d <= 6, From = "09:00", To = "20:00" }
                : new DayInput { On = true, From = SendWindows.Hm(x.Start), To = SendWindows.Hm(x.End) };
        }).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        var windows = new List<SendWindow>();
        if (Mode == "fasce")
        {
            for (var i = 0; i < Math.Min(7, Days.Count); i++)
            {
                var d = Days[i];
                if (!d.On) continue;
                var from = SendWindows.ParseHm(d.From); var to = SendWindows.ParseHm(d.To);
                if (from is null || to is null || to <= from)
                { Error = $"{SendWindows.DayNames[i + 1]}: scrivi un orario valido, es. 09:00 e 20:00 (la fine dopo l'inizio)."; return Page(); }
                windows.Add(new SendWindow(i + 1, from.Value, to.Value));
            }
            if (windows.Count == 0) { Error = "Scegli almeno un giorno, oppure seleziona «Sempre»."; return Page(); }
        }
        await _campaigns.SaveWindowsAsync(id, windows);
        var me = User.Scope();
        await _repos.AuditAsync(me, "gym.sendwindows", $"{Gym.Name}: {SendWindows.Describe(windows)}", HttpContext.Connection.RemoteIpAddress?.ToString(), Gym.OrganizationId, Gym.Id);
        TempData["Ok"] = "Orari di invio salvati: " + SendWindows.Describe(windows) + ".";
        return Redirect($"/Sedi/Orari/{id}");
    }
}
