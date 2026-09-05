using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;
using SnapCrm.Api.Services.Segmentation;

namespace SnapCrm.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SegmentsController(SnapCrmDbContext db, SegmentationService seg) : ControllerBase
{
    public record CreateSegmentDto(string Name, string RuleKey, string? RuleParamsJson, string? Description);

    [HttpGet("builtin")]
    public IActionResult BuiltIn() => Ok(SegmentationService.BuiltInKeys);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await db.Segments.Where(s => s.IsActive).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSegmentDto dto, CancellationToken ct)
    {
        var s = new Segment
        {
            Name = dto.Name,
            RuleKey = dto.RuleKey,
            RuleParamsJson = dto.RuleParamsJson,
            Description = dto.Description
        };
        db.Segments.Add(s);
        await db.SaveChangesAsync(ct);
        return Ok(s);
    }

    /// <summary>How many marketable customers a segment currently matches.</summary>
    [HttpGet("{id:int}/count")]
    public async Task<IActionResult> Count(int id, CancellationToken ct)
    {
        var s = await db.Segments.FindAsync([id], ct);
        if (s == null) return NotFound();
        return Ok(new { count = await seg.CountAsync(s, ct) });
    }
}
