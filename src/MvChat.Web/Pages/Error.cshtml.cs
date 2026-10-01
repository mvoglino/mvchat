using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MvChat.Web.Pages;

public class ErrorModel : PageModel
{
    public int Code { get; private set; } = 500;
    public void OnGet(int? code) { Code = code ?? 500; }
}
