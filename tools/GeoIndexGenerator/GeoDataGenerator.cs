namespace GeoIndexGenerator;

/// <summary>
/// Simple API for generating geo-index files programmatically.
/// </summary>
public static class GeoDataGenerator
{
    /// <summary>
    /// Generates a small test dataset (~50 major world cities).
    /// </summary>
    /// <param name="outputDir">Directory to write geo.geoindex and geo.geodata files.</param>
    public static async Task GenerateTestDataAsync(string outputDir)
    {
        await Program.RunAsync(
            download: false,
            input: null,
            output: new DirectoryInfo(outputDir),
            testOnly: true,
            citiesOnly: false,
            precision: 4);
    }

    /// <summary>
    /// Downloads cities15000.zip and generates index (~40K major cities).
    /// </summary>
    /// <param name="outputDir">Directory to write geo.geoindex and geo.geodata files.</param>
    public static async Task GenerateCitiesDataAsync(string outputDir)
    {
        await Program.RunAsync(
            download: true,
            input: null,
            output: new DirectoryInfo(outputDir),
            testOnly: false,
            citiesOnly: true,
            precision: 4);
    }

    /// <summary>
    /// Downloads allCountries.zip and generates full index (~12M locations).
    /// </summary>
    /// <param name="outputDir">Directory to write geo.geoindex and geo.geodata files.</param>
    public static async Task GenerateFullDataAsync(string outputDir)
    {
        await Program.RunAsync(
            download: true,
            input: null,
            output: new DirectoryInfo(outputDir),
            testOnly: false,
            citiesOnly: false,
            precision: 4);
    }

    /// <summary>
    /// Generates index from an existing GeoNames TSV file.
    /// </summary>
    /// <param name="geoNamesFile">Path to allCountries.txt or similar TSV file.</param>
    /// <param name="outputDir">Directory to write geo.geoindex and geo.geodata files.</param>
    public static async Task GenerateFromFileAsync(string geoNamesFile, string outputDir)
    {
        await Program.RunAsync(
            download: false,
            input: new FileInfo(geoNamesFile),
            output: new DirectoryInfo(outputDir),
            testOnly: false,
            citiesOnly: false,
            precision: 4);
    }
}
