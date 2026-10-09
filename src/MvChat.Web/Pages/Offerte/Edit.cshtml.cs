using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Catalog;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Offerte;

public class EditModel : PageModel
{
    private readonly CatalogRepo _catalog;
    private readonly Repos _repos;
    public EditModel(CatalogRepo catalog, Repos repos) { _catalog = catalog; _repos = repos; }

    [BindProperty] public OfferInput Input { get; set; } = new();
    public List<Gym> Gyms { get; private set; } = new();
    public bool IsNew => Input.Id == 0;

    public async Task<IActionResult> OnGetAsync(int? id, int? gym)
    {
        var me = User.Scope();
        Gyms = (await _repos.GymsAsync(me)).Where(g => g.IsActive).ToList();
        if (id is null)
        {
            Input.GymId = gym is int g && Gyms.Any(x => x.Id == g) ? g : me.GymId ?? Gyms.FirstOrDefault()?.Id ?? 0;
            return Page();
        }
        var o = await _catalog.OfferAsync(me, id.Value);
        if (o is null) return NotFound();
        Input = new OfferInput
        {
            Id = o.Id, GymId = o.GymId, Title = o.Title, Description = o.Description, Price = Fmt(o.Price), FullPrice = Fmt(o.FullPrice),
            PriceNote = o.PriceNote, Conditions = o.Conditions, ValidFrom = o.ValidFrom, ValidTo = o.ValidTo,
            MaxExtraDiscountPct = o.MaxExtraDiscountPct, ActionUrl = o.ActionUrl, BookUrl = o.BookUrl, IsActive = o.IsActive
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var me = User.Scope();
        Gyms = (await _repos.GymsAsync(me)).Where(g => g.IsActive).ToList();
        if (Input.Id != 0)
        {
            var existing = await _catalog.OfferAsync(me, Input.Id);
            if (existing is null) return NotFound();
            Input.GymId = existing.GymId; // l'attività di un'offerta esistente non si cambia
        }
        var gym = Gyms.FirstOrDefault(g => g.Id == Input.GymId);
        if (gym is null) ModelState.AddModelError("Input.GymId", "Scegli l'attività.");

        var price = ParseMoney(Input.Price, "Input.Price");
        var full = ParseMoney(Input.FullPrice, "Input.FullPrice");
        if (price is null && string.IsNullOrWhiteSpace(Input.Price) && string.IsNullOrWhiteSpace(Input.Description))
            ModelState.AddModelError("Input.Description", "Senza prezzo, descrivi almeno cosa comprende l'offerta.");
        if (full is not null && price is not null && full <= price)
            ModelState.AddModelError("Input.FullPrice", "Il prezzo pieno deve essere più alto del prezzo dell'offerta.");
        if (Input.ValidFrom is { } from && Input.ValidTo is { } to && to < from)
            ModelState.AddModelError("Input.ValidTo", "La fine non può essere prima dell'inizio.");
        if (!string.IsNullOrWhiteSpace(Input.ActionUrl) && (!Uri.TryCreate(Input.ActionUrl.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http")))
            ModelState.AddModelError("Input.ActionUrl", "Indirizzo non valido: deve iniziare con https://");
        if (!string.IsNullOrWhiteSpace(Input.BookUrl) && (!Uri.TryCreate(Input.BookUrl.Trim(), UriKind.Absolute, out var b) || b.Scheme is not ("https" or "http")))
            ModelState.AddModelError("Input.BookUrl", "Indirizzo non valido: deve iniziare con https://");
        if (!ModelState.IsValid) return Page();

        var id = await _catalog.SaveOfferAsync(new Offer
        {
            Id = Input.Id, OrganizationId = gym!.OrganizationId, GymId = gym.Id, Title = Input.Title.Trim(), Description = T(Input.Description),
            Price = price, FullPrice = full, PriceNote = T(Input.PriceNote), Conditions = T(Input.Conditions),
            ValidFrom = Input.ValidFrom, ValidTo = Input.ValidTo, MaxExtraDiscountPct = Input.MaxExtraDiscountPct,
            ActionUrl = T(Input.ActionUrl), BookUrl = T(Input.BookUrl), IsActive = Input.IsActive
        }, me.UserId);
        await _repos.AuditAsync(me, IsNew ? "offer.created" : "offer.updated", $"{Input.Title} · {Input.Price}", HttpContext.Connection.RemoteIpAddress?.ToString(), gym.OrganizationId, gym.Id);
        TempData["Ok"] = IsNew ? "Offerta creata." : "Offerta aggiornata.";
        return Redirect($"/Offerte?gym={gym.Id}");
    }

    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");
    private static string? Fmt(decimal? v) => v?.ToString("0.##", It);
    private static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Accetta "39", "39,90", "39.90", "1.200,00", "€ 39,90".</summary>
    private decimal? ParseMoney(string? raw, string field)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Replace("€", "").Replace(" ", "").Trim();
        if (s.Contains(',')) s = s.Replace(".", "").Replace(',', '.');
        else if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{1,3}(\.\d{3})+$")) s = s.Replace(".", ""); // 1.200 = milleduecento
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0 && v < 100000) return Math.Round(v, 2);
        ModelState.AddModelError(field, "Scrivi un importo, ad esempio 39,90.");
        return null;
    }

    public class OfferInput
    {
        public int Id { get; set; }
        public int GymId { get; set; }
        [Required(ErrorMessage = "Dai un titolo all'offerta."), StringLength(150)] public string Title { get; set; } = "";
        [StringLength(2000)] public string? Description { get; set; }
        public string? Price { get; set; }
        public string? FullPrice { get; set; }
        [StringLength(100)] public string? PriceNote { get; set; }
        [StringLength(2000)] public string? Conditions { get; set; }
        [DataType(DataType.Date)] public DateTime? ValidFrom { get; set; }
        [DataType(DataType.Date)] public DateTime? ValidTo { get; set; }
        [Range(0, 30, ErrorMessage = "Lo sconto extra va da 0 a 30%.")] public int MaxExtraDiscountPct { get; set; }
        [StringLength(400)] public string? ActionUrl { get; set; }
        [StringLength(400)] public string? BookUrl { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
