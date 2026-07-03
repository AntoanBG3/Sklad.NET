using Sklad.Models;

namespace Sklad.Data;

public static class DbInitializer
{
    public static void Seed(SkladDbContext context)
    {
        if (context.Tires.Any())
            return;

        var tires = new List<Tire>
        {
            new() { Sku = "MIC-20555R16-S", Barcode = "3528706782900", Brand = "Michelin",   Model = "Primacy 4",          Width = 205, Profile = 55, Diameter = 16, Season = Season.Summer,    Type = TireType.New,       UnitPrice = 189.99m, Quantity = 40, MinStock = 10, Location = "A1" },
            new() { Sku = "MIC-20555R16-W", Barcode = "3528706782901", Brand = "Michelin",   Model = "Alpin 6",             Width = 205, Profile = 55, Diameter = 16, Season = Season.Winter,    Type = TireType.New,       UnitPrice = 205.50m, Quantity = 25, MinStock = 8,  Location = "A2" },
            new() { Sku = "BRI-20560R16-S", Barcode = "3286340781200", Brand = "Bridgestone", Model = "Turanza T005",       Width = 205, Profile = 60, Diameter = 16, Season = Season.Summer,    Type = TireType.New,       UnitPrice = 175.00m, Quantity = 30, MinStock = 10, Location = "B1" },
            new() { Sku = "BRI-22545R17-S", Barcode = "3286340781201", Brand = "Bridgestone", Model = "Potenza Sport",      Width = 225, Profile = 45, Diameter = 17, Season = Season.Summer,    Type = TireType.New,       UnitPrice = 220.00m, Quantity = 18, MinStock = 6,  Location = "B2" },
            new() { Sku = "GOO-20550R17-A", Barcode = "5452000579010", Brand = "Goodyear",   Model = "Vector 4Seasons G3",  Width = 205, Profile = 50, Diameter = 17, Season = Season.AllSeason, Type = TireType.New,       UnitPrice = 198.75m, Quantity = 35, MinStock = 12, Location = "C1" },
            new() { Sku = "GOO-22545R18-S", Barcode = "5452000579011", Brand = "Goodyear",   Model = "Eagle F1 Asymmetric", Width = 225, Profile = 45, Diameter = 18, Season = Season.Summer,    Type = TireType.New,       UnitPrice = 245.00m, Quantity = 12, MinStock = 5,  Location = "C2" },
            new() { Sku = "CON-19565R15-W", Barcode = "4019238012340", Brand = "Continental", Model = "WinterContact TS 870",Width = 195, Profile = 65, Diameter = 15, Season = Season.Winter,    Type = TireType.New,       UnitPrice = 155.00m, Quantity = 50, MinStock = 15, Location = "D1" },
            new() { Sku = "CON-20555R16-A", Barcode = "4019238012341", Brand = "Continental", Model = "AllSeasonContact 2", Width = 205, Profile = 55, Diameter = 16, Season = Season.AllSeason, Type = TireType.New,       UnitPrice = 185.00m, Quantity = 20, MinStock = 8,  Location = "D2" },
            new() { Sku = "PIR-21540R18-S", Barcode = "8019227233490", Brand = "Pirelli",    Model = "P Zero",              Width = 215, Profile = 40, Diameter = 18, Season = Season.Summer,    Type = TireType.New,       UnitPrice = 265.00m, Quantity = 8,  MinStock = 4,  Location = "E1" },
            new() { Sku = "PIR-20555R16-W", Barcode = "8019227233491", Brand = "Pirelli",    Model = "Sottozero 3",         Width = 205, Profile = 55, Diameter = 16, Season = Season.Winter,    Type = TireType.New,       UnitPrice = 215.00m, Quantity = 15, MinStock = 6,  Location = "E2" },
            new() { Sku = "HAN-18565R15-S", Barcode = "8808563390010", Brand = "Hankook",    Model = "Kinergy Eco 2",       Width = 185, Profile = 65, Diameter = 15, Season = Season.Summer,    Type = TireType.New,       UnitPrice = 98.00m,  Quantity = 60, MinStock = 20, Location = "F1" },
            new() { Sku = "HAN-20555R16-W", Barcode = "8808563390011", Brand = "Hankook",    Model = "Winter i*cept RS2",   Width = 205, Profile = 55, Diameter = 16, Season = Season.Winter,    Type = TireType.New,       UnitPrice = 112.00m, Quantity = 45, MinStock = 15, Location = "F2" },
            new() { Sku = "NOK-20555R16-W", Barcode = "6419440179010", Brand = "Nokian",     Model = "Hakkapeliitta 10",    Width = 205, Profile = 55, Diameter = 16, Season = Season.Winter,    Type = TireType.New,       UnitPrice = 230.00m, Quantity = 4,  MinStock = 8,  Location = "G1" },
            new() { Sku = "FAL-22545R17-S", Barcode = "4250427420010", Brand = "Falken",     Model = "Azenis FK520",        Width = 225, Profile = 45, Diameter = 17, Season = Season.Summer,    Type = TireType.Retreaded, UnitPrice = 89.00m,  Quantity = 22, MinStock = 6,  Location = "G2" },
            new() { Sku = "TOY-20555R16-A", Barcode = "4981910796010", Brand = "Toyo",       Model = "Celsius",             Width = 205, Profile = 55, Diameter = 16, Season = Season.AllSeason, Type = TireType.New,       UnitPrice = 160.00m, Quantity = 28, MinStock = 10, Location = "H1" },
        };

        foreach (var tire in tires.Where(t => t.Quantity > 0))
        {
            tire.StockMovements.Add(new StockMovement
            {
                MovementType = MovementType.Adjustment,
                Quantity = tire.Quantity,
                Date = DateTime.UtcNow,
                Note = "Opening stock",
                UserName = "seed"
            });
        }

        context.Tires.AddRange(tires);
        context.SaveChanges();
    }
}
