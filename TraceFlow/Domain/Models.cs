namespace TraceFlow.Domain;

public enum Station { Assembly, Test, Packing, Shipped }

public class ProductionUnit
{
    public int Id { get; set; }
    public string SerialNumber { get; set; } = "";
    public Station Station { get; set; } = Station.Assembly;
    public bool IsDefect { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = "";
    public List<UnitEvent> Events { get; set; } = new();
}

/// <summary>Traceability history entry.</summary>
public class UnitEvent
{
    public int Id { get; set; }
    public int ProductionUnitId { get; set; }
    public string Action { get; set; } = "";
    public string User { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public record UnitDto(int Id, string SerialNumber, string Station, bool IsDefect, DateTime UpdatedAt, string UpdatedBy);
public record EventDto(string Action, string User, DateTime At);
