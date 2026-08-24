namespace PCStore.Core.Entities;

public class TechnicalSpec
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public string? Socket { get; set; }
    public string? Chipset { get; set; }
    public string? FormFactor { get; set; }
    public string? RamType { get; set; }
    public int? RamBusSpeed { get; set; }
    public int? RamSlots { get; set; }
    public int? TdpWattage { get; set; }
    public int? RecommendedPsu { get; set; }
    public int? LengthMm { get; set; }
    public string? AdditionalSpecsJson { get; set; }
}