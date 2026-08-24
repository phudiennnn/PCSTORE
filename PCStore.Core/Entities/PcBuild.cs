using System;
using System.Collections.Generic;
using System.Text;

namespace PCStore.Core.Entities;

public class PcBuild
{
    public int Id { get; set; }
    public string BuildName { get; set; } = "Cấu hình tùy chọn";
    public decimal TotalPrice { get; set; }
    public int EstimatedWattage { get; set; }
    public bool IsCompatible { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? UserId { get; set; }
    public User? User { get; set; }

    public ICollection<PcBuildItem> Items { get; set; } = new List<PcBuildItem>();
}

