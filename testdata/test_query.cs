using Microsoft.EntityFrameworkCore;
using Clinic.Infrastructure.Data;
using Clinic.Domain.Entities;

var options = new DbContextOptionsBuilder<ClinicDbContext>()
    .UseSqlite("Data Source=E:\\个人诊所处方系统\\clinic.db")
    .LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information)
    .Options;

using var db = new ClinicDbContext(options);
try
{
    Console.WriteLine("Querying DrugMaster...");
    var drugs = await db.DrugMasters.ToListAsync();
    Console.WriteLine($"Got {drugs.Count} drugs");
    foreach (var d in drugs.Take(3))
    {
        Console.WriteLine($"  {d.Id}: {d.GenericNameCn} | antibiotic={d.IsAntibiotic} | level={d.AntibioticLevel} | toxic={d.IsToxicDrug}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
