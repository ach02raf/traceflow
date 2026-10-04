using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TraceFlow.Data;
using TraceFlow.Domain;
using TraceFlow.Hubs;

namespace TraceFlow.Controllers;

[ApiController, Route("api/units"), Authorize, AutoValidateAntiforgeryToken]
public class UnitsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<TraceHub> _hub;
    public UnitsController(AppDbContext db, IHubContext<TraceHub> hub) { _db = db; _hub = hub; }

    private static UnitDto ToDto(ProductionUnit u) =>
        new(u.Id, u.SerialNumber, u.Station.ToString(), u.IsDefect, u.UpdatedAt, u.UpdatedBy);

    [HttpGet]
    public async Task<IEnumerable<UnitDto>> Get()
    {
        var list = await _db.Units.AsNoTracking().OrderByDescending(u => u.UpdatedAt).ToListAsync();
        return list.Select(ToDto);
    }

    [HttpGet("{id:int}/history")]
    public async Task<IEnumerable<EventDto>> History(int id)
    {
        var list = await _db.Events.AsNoTracking()
            .Where(e => e.ProductionUnitId == id).OrderByDescending(e => e.At).ToListAsync();
        return list.Select(e => new EventDto(e.Action, e.User, e.At));
    }

    public record CreateRequest(string SerialNumber);

    [HttpPost]
    public async Task<IActionResult> Create(CreateRequest req)
    {
        var serial = req.SerialNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(serial)) return BadRequest("Serial required");
        if (serial.Length > 50) return BadRequest("Serial too long");
        if (await _db.Units.AnyAsync(u => u.SerialNumber == serial)) return Conflict("Serial already exists");

        var user = User.Identity!.Name!;
        var unit = new ProductionUnit { SerialNumber = serial, UpdatedBy = user };
        unit.Events.Add(new UnitEvent { Action = "Created", User = user });
        _db.Units.Add(unit);
        await _db.SaveChangesAsync();

        var dto = ToDto(unit);
        await _hub.Clients.All.SendAsync("UnitChanged", dto);
        return Ok(dto);
    }

    [HttpPost("{id:int}/advance")]
    public async Task<IActionResult> Advance(int id)
    {
        var unit = await _db.Units.FindAsync(id);
        if (unit is null) return NotFound();
        if (unit.IsDefect) return BadRequest("Defective unit cannot advance");
        var next = Workflow.Next(unit.Station);
        if (next is null) return BadRequest("Already shipped");

        var user = User.Identity!.Name!;
        unit.Station = next.Value; unit.UpdatedAt = DateTime.UtcNow; unit.UpdatedBy = user;
        _db.Events.Add(new UnitEvent { ProductionUnitId = id, Action = $"Moved to {next}", User = user });
        await _db.SaveChangesAsync();

        var dto = ToDto(unit);
        await _hub.Clients.All.SendAsync("UnitChanged", dto);
        return Ok(dto);
    }

    [HttpPost("{id:int}/defect"), Authorize(Roles = "Supervisor")]
    public async Task<IActionResult> Defect(int id)
    {
        var unit = await _db.Units.FindAsync(id);
        if (unit is null) return NotFound();
        var user = User.Identity!.Name!;
        unit.IsDefect = true; unit.UpdatedAt = DateTime.UtcNow; unit.UpdatedBy = user;
        _db.Events.Add(new UnitEvent { ProductionUnitId = id, Action = "Flagged defect", User = user });
        await _db.SaveChangesAsync();

        var dto = ToDto(unit);
        await _hub.Clients.All.SendAsync("UnitChanged", dto);
        return Ok(dto);
    }
}
