using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Infrastructure;

namespace MvChat.Web.Pages.Impostazioni;

/// <summary>Le ultime righe del registro tecnico (App_Data/logs): avvii, chiusure ed errori del programma. Solo MVitalia.</summary>
public class RegistroModel : PageModel
{
    private readonly AppConfigStore _config;
    public RegistroModel(AppConfigStore config) => _config = config;
    public List<string> Lines { get; private set; } = new();
    public bool OnlyProblems { get; private set; }

    public void OnGet(bool problemi = false)
    {
        OnlyProblems = problemi;
        var all = FileLogProvider.Tail(_config.DataDir, 2000);
        Lines = (problemi ? all.Where(l => l.Contains(" ERRORE ") || l.Contains(" GRAVE ") || l.Contains(" AVVISO ")) : all)
            .Reverse().Take(300).ToList();
    }
}
