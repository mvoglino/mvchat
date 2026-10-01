using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MvChat.Web.Pages;

[Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public int Code { get; private set; } = 500;
    public void OnGet(int? code) { Code = code ?? 500; }
    public void OnPost(int? code) { Code = code ?? 500; }
}
