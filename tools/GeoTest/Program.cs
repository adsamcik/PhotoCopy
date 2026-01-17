using Microsoft.Extensions.Logging;
using PhotoCopy.Configuration;
using PhotoCopy.Files.Geo;

// Test coordinates provided by user
var testCases = new (string Name, double Lat, double Lon, string ExpectedCity)[]
{
    ("Prague1", 50.07973397930704, 14.468244770604457, "Prague"),
    ("Prague2", 50.06226873602398, 14.346925887282561, "Prague"),
    ("Krivoklat", 49.981671617113385, 13.861159687141079, "Křivoklátsko?"),
    ("Plzen", 49.736701361665006, 13.36733379985312, "Plzen"),
    ("Manching", 48.71496705449036, 11.497902604274653, "Manching"),
    ("GrattStadt", 50.37552419454665, 10.837585451309092, "GrattStadt"),
    ("Edgerton", 43.41371984309537, -106.24696672369056, "Edgerton"),
    ("Yellowstone", 44.524728360650606, -110.50920190051461, "Yellowstone?"),
};

// TieredGeocodingService looks for geo.geoindex + geo.geodata files in the data directory
var dataDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "PhotoCopy", "data"));
var indexPath = Path.Combine(dataDir, "geo.geoindex");
var dataPath = Path.Combine(dataDir, "geo.geodata");

if (!File.Exists(indexPath) || !File.Exists(dataPath))
{
    Console.WriteLine($"ERROR: Geo-index files not found at: {dataDir}");
    Console.WriteLine("Run the GeoIndexGenerator tool to create index files.");
    return 1;
}

Console.WriteLine($"Loading geo-index from: {dataDir}");

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger<TieredGeocodingService>();

// Set GeonamesPath to the data directory so the service can find the index files
var config = new PhotoCopyConfig { GeonamesPath = indexPath };
var service = new TieredGeocodingService(logger, config);

await service.InitializeAsync();

Console.WriteLine("\n=== Coordinate Lookup Results ===\n");
Console.WriteLine($"{"Name",-15} {"Expected",-15} {"District",-25} {"City",-20} {"Country",-8} {"Pop"}");
Console.WriteLine(new string('-', 100));

foreach (var (name, lat, lon, expectedCity) in testCases)
{
    var result = service.ReverseGeocode(lat, lon);
    if (result != null)
    {
        Console.WriteLine($"{name,-15} {expectedCity,-15} {result.District ?? "null",-25} {result.City ?? "null",-20} {result.Country,-8} {result.Population}");
    }
    else
    {
        Console.WriteLine($"{name,-15} {expectedCity,-15} NO RESULT");
    }
}

return 0;
