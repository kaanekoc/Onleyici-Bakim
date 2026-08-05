using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace OnleyiciBakim.Data.BakimYonetimi.Entities;

public partial class Firmalar
{
    [Key]
    [StringLength(20)]
    public string FirmaId { get; set; } = null!;

    [StringLength(200)]
    public string? FirmaAdi { get; set; }

    [StringLength(50)]
    public string? FirmaKodu { get; set; }

    public bool AktifMi { get; set; }

    [StringLength(50)]
    public string? VeriKaynagi { get; set; }

    [InverseProperty("Firma")]
    public virtual ICollection<Makineler> Makineler { get; set; } = new List<Makineler>();

    [InverseProperty("Firma")]
    public virtual ICollection<Subeler> Subeler { get; set; } = new List<Subeler>();
}
