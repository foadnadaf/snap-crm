using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;

namespace SnapCrm.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CustomersController(SnapCrmDbContext db) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var total = await db.Customers.CountAsync(ct);
        var mailable = await db.Customers.CountAsync(c =>
            c.EmailConsent == ConsentStatus.OptedIn && !c.EmailHardBounced && c.Email != null, ct);
        var dormant = await db.Customers.CountAsync(c => c.DaysSinceLastOrder >= 30, ct);
        var vip = await db.Customers.CountAsync(c => c.TotalSpent >= 200m, ct);
        return Ok(new { total, mailable, dormant, vip });
    }

    [HttpGet]
    public async Task<IActionResult> List(string? q, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.Customers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(c => (c.Email ?? "").Contains(q) || (c.FirstName ?? "").Contains(q));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.LastOrderAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.Email, c.FirstName, c.Plz, c.OrderCount, c.TotalSpent,
                c.LastOrderAt, c.DaysSinceLastOrder, c.EmailConsent
            }).ToListAsync(ct);
        return Ok(new { total, page, pageSize, items });
    }
}
