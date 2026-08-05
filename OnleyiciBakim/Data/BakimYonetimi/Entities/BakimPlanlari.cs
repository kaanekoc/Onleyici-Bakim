using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace OnleyiciBakim.Data.BakimYonetimi.Entities;

[Index("MakineId", Name = "IX_BakimPlanlari_MakineId")]
[Index("PlanlananTarih", Name = "IX_BakimPlanlari_PlanlananTarih")]
[Index("SorumluPersonelId", Name = "IX_BakimPlanlari_SorumluPersonelId")]
public partial class BakimPlanlari
{
    [Key]
    [StringLength(20)]
    public string PlanId { get; set; } = null!;

    [StringLength(20)]
    public string MakineId { get; set; } = null!;

    [StringLength(20)]
    public string? BilesenKodu { get; set; }

    [StringLength(100)]
    public string? BilesenAdi { get; set; }

    public DateTime PlanlananTarih { get; set; }

    [StringLength(30)]
    public string? BakimTuru { get; set; }

    [StringLength(20)]
    public string? Oncelik { get; set; }

    public int TahminiSureDk { get; set; }

    public int TahminiMaliyetTl { get; set; }

    [StringLength(20)]
    public string? SorumluPersonelId { get; set; }

    [StringLength(30)]
    public string? Durum { get; set; }

    [StringLength(1000)]
    public string? Gerekce { get; set; }

    [StringLength(50)]
    public string? VeriKaynagi { get; set; }

    [InverseProperty("Plan")]
    public virtual ICollection<BakimPlanGorevleri> BakimPlanGorevleri { get; set; } = new List<BakimPlanGorevleri>();

    [InverseProperty("Plan")]
    public virtual ICollection<IsEmirleri> IsEmirleri { get; set; } = new List<IsEmirleri>();

    [ForeignKey("MakineId")]
    [InverseProperty("BakimPlanlari")]
    public virtual Makineler Makine { get; set; } = null!;

    [ForeignKey("SorumluPersonelId")]
    [InverseProperty("BakimPlanlari")]
    public virtual Personeller? SorumluPersonel { get; set; }
}
