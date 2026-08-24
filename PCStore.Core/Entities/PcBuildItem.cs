using System;
using System.Collections.Generic;
using System.Text;

namespace PCStore.Core.Entities;

public class PcBuildItem
{
    public int Id { get; set; }
    public int PcBuildId { get; set; }
    public PcBuild? PcBuild { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}