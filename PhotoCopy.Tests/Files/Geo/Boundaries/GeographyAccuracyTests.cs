using PhotoCopy.Files.Geo.Boundaries;

namespace PhotoCopy.Tests.Files.Geo.Boundaries;

/// <summary>
/// Geography accuracy tests using real-world coordinates.
/// These tests verify that simplified country boundaries correctly identify
/// locations for various geographic scenarios including capitals, borders,
/// enclaves, exclaves, island nations, and edge cases.
/// 
/// Coordinates sourced from Wikipedia and Google Maps for accuracy.
/// </summary>
public class GeographyAccuracyTests
{
    #region World Capitals - Major Countries

    [Test]
    public async Task Paris_IsInFrance()
    {
        // Eiffel Tower: 48.8584° N, 2.2945° E
        var france = CreateFranceBoundary();
        var result = PointInPolygon.IsPointInCountry(48.8584, 2.2945, france);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task London_IsInUnitedKingdom()
    {
        // Big Ben: 51.5007° N, 0.1246° W (-0.1246)
        var uk = CreateUnitedKingdomBoundary();
        var result = PointInPolygon.IsPointInCountry(51.5007, -0.1246, uk);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Berlin_IsInGermany()
    {
        // Brandenburg Gate: 52.5163° N, 13.3777° E
        var germany = CreateGermanyBoundary();
        var result = PointInPolygon.IsPointInCountry(52.5163, 13.3777, germany);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Madrid_IsInSpain()
    {
        // Royal Palace of Madrid: 40.4180° N, 3.7143° W (-3.7143)
        var spain = CreateSpainBoundary();
        var result = PointInPolygon.IsPointInCountry(40.4180, -3.7143, spain);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Rome_IsInItaly()
    {
        // Colosseum: 41.8902° N, 12.4922° E
        var italy = CreateItalyBoundary();
        var result = PointInPolygon.IsPointInCountry(41.8902, 12.4922, italy);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Tokyo_IsInJapan()
    {
        // Tokyo Imperial Palace: 35.6852° N, 139.7528° E
        var japan = CreateJapanBoundary();
        var result = PointInPolygon.IsPointInCountry(35.6852, 139.7528, japan);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Canberra_IsInAustralia()
    {
        // Parliament House: 35.3080° S (-35.3080), 149.1245° E
        var australia = CreateAustraliaBoundary();
        var result = PointInPolygon.IsPointInCountry(-35.3080, 149.1245, australia);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task WashingtonDC_IsInUSA()
    {
        // White House: 38.8977° N, 77.0365° W (-77.0365)
        var usa = CreateUSABoundary();
        var result = PointInPolygon.IsPointInCountry(38.8977, -77.0365, usa);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Ottawa_IsInCanada()
    {
        // Parliament Hill: 45.4236° N, 75.7009° W (-75.7009)
        var canada = CreateCanadaBoundary();
        var result = PointInPolygon.IsPointInCountry(45.4236, -75.7009, canada);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Moscow_IsInRussia()
    {
        // Kremlin: 55.7520° N, 37.6175° E
        var russia = CreateRussiaBoundary();
        var result = PointInPolygon.IsPointInCountry(55.7520, 37.6175, russia);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Beijing_IsInChina()
    {
        // Forbidden City: 39.9163° N, 116.3972° E
        var china = CreateChinaBoundary();
        var result = PointInPolygon.IsPointInCountry(39.9163, 116.3972, china);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task NewDelhi_IsInIndia()
    {
        // India Gate: 28.6129° N, 77.2295° E
        var india = CreateIndiaBoundary();
        var result = PointInPolygon.IsPointInCountry(28.6129, 77.2295, india);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Brasilia_IsInBrazil()
    {
        // Praça dos Três Poderes: 15.7997° S (-15.7997), 47.8644° W (-47.8644)
        var brazil = CreateBrazilBoundary();
        var result = PointInPolygon.IsPointInCountry(-15.7997, -47.8644, brazil);
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region Border Cities - International Boundaries

    [Test]
    public async Task TijuanaMexico_IsInMexicoNotUSA()
    {
        // Tijuana, Mexico: 32.5149° N, 117.0382° W
        var mexico = CreateMexicoBoundary();
        var usa = CreateUSASouthernBorder();
        
        var inMexico = PointInPolygon.IsPointInCountry(32.5149, -117.0382, mexico);
        var inUSA = PointInPolygon.IsPointInCountry(32.5149, -117.0382, usa);
        
        await Assert.That(inMexico).IsTrue();
        await Assert.That(inUSA).IsFalse();
    }

    [Test]
    public async Task SanDiegoCalifornia_IsInUSANotMexico()
    {
        // San Diego, USA: 32.7157° N, 117.1611° W
        var usa = CreateUSASouthernBorder();
        var mexico = CreateMexicoBoundary();
        
        var inUSA = PointInPolygon.IsPointInCountry(32.7157, -117.1611, usa);
        var inMexico = PointInPolygon.IsPointInCountry(32.7157, -117.1611, mexico);
        
        await Assert.That(inUSA).IsTrue();
        await Assert.That(inMexico).IsFalse();
    }

    [Test]
    public async Task NiagaraFallsCanada_IsInCanadaNotUSA()
    {
        // Niagara Falls, Ontario, Canada: 43.0896° N, 79.0849° W
        var canada = CreateCanadaSouthernBorder();
        var usa = CreateUSANorthernBorder();
        
        var inCanada = PointInPolygon.IsPointInCountry(43.0896, -79.0849, canada);
        var inUSA = PointInPolygon.IsPointInCountry(43.0896, -79.0849, usa);
        
        await Assert.That(inCanada).IsTrue();
        await Assert.That(inUSA).IsFalse();
    }

    [Test]
    public async Task NiagaraFallsNY_IsInUSANotCanada()
    {
        // Niagara Falls, NY, USA: 43.0962° N, 79.0377° W
        // Note: The border runs along the Niagara River
        // Using simplified boundaries that don't overlap
        var usa = CreateUSANorthernBorder();
        var canada = CreateCanadaSouthernBorderNonOverlapping();
        
        var inUSA = PointInPolygon.IsPointInCountry(43.0962, -79.0377, usa);
        var inCanada = PointInPolygon.IsPointInCountry(43.0962, -79.0377, canada);
        
        await Assert.That(inUSA).IsTrue();
        await Assert.That(inCanada).IsFalse();
    }

    [Test]
    public async Task BaselSwitzerland_IsInSwitzerland()
    {
        // Basel, Switzerland: 47.5596° N, 7.5886° E (tripoint with France & Germany)
        var switzerland = CreateSwitzerlandBoundary();
        var result = PointInPolygon.IsPointInCountry(47.5596, 7.5886, switzerland);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Strasbourg_IsInFranceNotGermany()
    {
        // Strasbourg Cathedral: 48.5818° N, 7.7508° E
        var france = CreateFranceAlsaceBoundary();
        var germany = CreateGermanyBadenBoundary();
        
        var inFrance = PointInPolygon.IsPointInCountry(48.5818, 7.7508, france);
        var inGermany = PointInPolygon.IsPointInCountry(48.5818, 7.7508, germany);
        
        await Assert.That(inFrance).IsTrue();
        await Assert.That(inGermany).IsFalse();
    }

    [Test]
    public async Task Kehl_IsInGermanyNotFrance()
    {
        // Kehl, Germany: 48.5728° N, 7.8153° E
        var germany = CreateGermanyBadenBoundary();
        var france = CreateFranceAlsaceBoundary();
        
        var inGermany = PointInPolygon.IsPointInCountry(48.5728, 7.8153, germany);
        var inFrance = PointInPolygon.IsPointInCountry(48.5728, 7.8153, france);
        
        await Assert.That(inGermany).IsTrue();
        await Assert.That(inFrance).IsFalse();
    }

    [Test]
    public async Task Windsor_IsInCanadaNotUSA()
    {
        // Windsor, Ontario: 42.3149° N, 83.0364° W
        var canada = CreateCanadaWindsorArea();
        var usa = CreateUSADetroitArea();
        
        var inCanada = PointInPolygon.IsPointInCountry(42.3149, -83.0364, canada);
        var inUSA = PointInPolygon.IsPointInCountry(42.3149, -83.0364, usa);
        
        await Assert.That(inCanada).IsTrue();
        await Assert.That(inUSA).IsFalse();
    }

    [Test]
    public async Task Detroit_IsInUSANotCanada()
    {
        // Detroit, Michigan: 42.3314° N, 83.0458° W
        var usa = CreateUSADetroitArea();
        var canada = CreateCanadaWindsorArea();
        
        var inUSA = PointInPolygon.IsPointInCountry(42.3314, -83.0458, usa);
        var inCanada = PointInPolygon.IsPointInCountry(42.3314, -83.0458, canada);
        
        await Assert.That(inUSA).IsTrue();
        await Assert.That(inCanada).IsFalse();
    }

    #endregion

    #region Enclaves and Exclaves

    [Test]
    public async Task VaticanCity_IsNotInItaly()
    {
        // St. Peter's Basilica: 41.9022° N, 12.4539° E
        var italyWithVaticanHole = CreateItalyWithVaticanHole();
        var result = PointInPolygon.IsPointInCountry(41.9022, 12.4539, italyWithVaticanHole);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task VaticanCity_IsInVatican()
    {
        // St. Peter's Basilica: 41.9022° N, 12.4539° E
        var vatican = CreateVaticanBoundary();
        var result = PointInPolygon.IsPointInCountry(41.9022, 12.4539, vatican);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task SanMarino_IsNotInItaly()
    {
        // San Marino City Center: 43.9333° N, 12.4500° E
        var italyWithSanMarinoHole = CreateItalyWithSanMarinoHole();
        var result = PointInPolygon.IsPointInCountry(43.9333, 12.4500, italyWithSanMarinoHole);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task SanMarino_IsInSanMarino()
    {
        // San Marino City Center: 43.9333° N, 12.4500° E
        var sanMarino = CreateSanMarinoBoundary();
        var result = PointInPolygon.IsPointInCountry(43.9333, 12.4500, sanMarino);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Lesotho_IsNotInSouthAfrica()
    {
        // Maseru, Lesotho: 29.3167° S (-29.3167), 27.4833° E
        var southAfricaWithLesothoHole = CreateSouthAfricaWithLesothoHole();
        var result = PointInPolygon.IsPointInCountry(-29.3167, 27.4833, southAfricaWithLesothoHole);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Lesotho_IsInLesotho()
    {
        // Maseru, Lesotho: 29.3167° S (-29.3167), 27.4833° E
        var lesotho = CreateLesothoBoundary();
        var result = PointInPolygon.IsPointInCountry(-29.3167, 27.4833, lesotho);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Monaco_IsNotInFrance()
    {
        // Monte Carlo Casino: 43.7396° N, 7.4281° E
        var franceWithMonacoHole = CreateFranceWithMonacoHole();
        var result = PointInPolygon.IsPointInCountry(43.7396, 7.4281, franceWithMonacoHole);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Monaco_IsInMonaco()
    {
        // Monte Carlo Casino: 43.7396° N, 7.4281° E
        var monaco = CreateMonacoBoundary();
        var result = PointInPolygon.IsPointInCountry(43.7396, 7.4281, monaco);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Kaliningrad_IsInRussia()
    {
        // Kaliningrad, Russian exclave: 54.7104° N, 20.4522° E
        var russia = CreateRussiaWithKaliningrad();
        var result = PointInPolygon.IsPointInCountry(54.7104, 20.4522, russia);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Kaliningrad_IsNotInPolandOrLithuania()
    {
        // Kaliningrad: 54.7104° N, 20.4522° E
        // Poland's northern boundary is ~54.8°N, but Kaliningrad extends north of it
        // Using accurate boundaries that exclude Kaliningrad Oblast
        var poland = CreatePolandBoundaryAccurate();
        var lithuania = CreateLithuaniaBoundary();
        
        var inPoland = PointInPolygon.IsPointInCountry(54.7104, 20.4522, poland);
        var inLithuania = PointInPolygon.IsPointInCountry(54.7104, 20.4522, lithuania);
        
        await Assert.That(inPoland).IsFalse();
        await Assert.That(inLithuania).IsFalse();
    }

    #endregion

    #region Island Nations

    [Test]
    public async Task Honolulu_IsInUSA()
    {
        // Pearl Harbor, Hawaii: 21.3469° N, 157.9743° W (-157.9743)
        var usa = CreateUSAWithHawaii();
        var result = PointInPolygon.IsPointInCountry(21.3469, -157.9743, usa);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Osaka_IsInJapan()
    {
        // Osaka Castle: 34.6873° N, 135.5262° E
        var japan = CreateJapanBoundary();
        var result = PointInPolygon.IsPointInCountry(34.6873, 135.5262, japan);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Auckland_IsInNewZealand()
    {
        // Auckland Sky Tower: 36.8485° S (-36.8485), 174.7633° E
        var newZealand = CreateNewZealandBoundary();
        var result = PointInPolygon.IsPointInCountry(-36.8485, 174.7633, newZealand);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Wellington_IsInNewZealand()
    {
        // Beehive (Parliament): 41.2784° S (-41.2784), 174.7760° E
        var newZealand = CreateNewZealandBoundary();
        var result = PointInPolygon.IsPointInCountry(-41.2784, 174.7760, newZealand);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Reykjavik_IsInIceland()
    {
        // Hallgrímskirkja: 64.1417° N, 21.9264° W (-21.9264)
        var iceland = CreateIcelandBoundary();
        var result = PointInPolygon.IsPointInCountry(64.1417, -21.9264, iceland);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Havana_IsInCuba()
    {
        // Capitolio Nacional: 23.1365° N, 82.3589° W (-82.3589)
        var cuba = CreateCubaBoundary();
        var result = PointInPolygon.IsPointInCountry(23.1365, -82.3589, cuba);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Singapore_IsInSingapore()
    {
        // Marina Bay Sands: 1.2838° N, 103.8591° E
        var singapore = CreateSingaporeBoundary();
        var result = PointInPolygon.IsPointInCountry(1.2838, 103.8591, singapore);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Manila_IsInPhilippines()
    {
        // Rizal Park: 14.5830° N, 120.9770° E
        var philippines = CreatePhilippinesBoundary();
        var result = PointInPolygon.IsPointInCountry(14.5830, 120.9770, philippines);
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region Tiny Countries

    [Test]
    public async Task VaduzLiechtenstein_IsInLiechtenstein()
    {
        // Vaduz Castle: 47.1392° N, 9.5215° E
        var liechtenstein = CreateLiechtensteinBoundary();
        var result = PointInPolygon.IsPointInCountry(47.1392, 9.5215, liechtenstein);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Andorra_IsInAndorra()
    {
        // Andorra la Vella: 42.5063° N, 1.5218° E
        var andorra = CreateAndorraBoundary();
        var result = PointInPolygon.IsPointInCountry(42.5063, 1.5218, andorra);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Luxembourg_IsInLuxembourg()
    {
        // Grand Ducal Palace: 49.6116° N, 6.1319° E
        var luxembourg = CreateLuxembourgBoundary();
        var result = PointInPolygon.IsPointInCountry(49.6116, 6.1319, luxembourg);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Malta_IsInMalta()
    {
        // Valletta: 35.8989° N, 14.5146° E
        var malta = CreateMaltaBoundary();
        var result = PointInPolygon.IsPointInCountry(35.8989, 14.5146, malta);
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region Multi-Timezone Countries (Large Nations)

    [Test]
    public async Task Vladivostok_IsInRussia()
    {
        // Vladivostok, Far East Russia: 43.1198° N, 131.8869° E
        var russia = CreateRussiaBoundary();
        var result = PointInPolygon.IsPointInCountry(43.1198, 131.8869, russia);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task LosAngeles_IsInUSA()
    {
        // Hollywood Sign: 34.1341° N, 118.3215° W (-118.3215)
        var usa = CreateUSABoundary();
        var result = PointInPolygon.IsPointInCountry(34.1341, -118.3215, usa);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task NewYork_IsInUSA()
    {
        // Statue of Liberty: 40.6892° N, 74.0445° W (-74.0445)
        var usa = CreateUSABoundary();
        var result = PointInPolygon.IsPointInCountry(40.6892, -74.0445, usa);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Shanghai_IsInChina()
    {
        // Oriental Pearl Tower: 31.2397° N, 121.4998° E
        var china = CreateChinaBoundary();
        var result = PointInPolygon.IsPointInCountry(31.2397, 121.4998, china);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Mumbai_IsInIndia()
    {
        // Gateway of India: 18.9220° N, 72.8347° E
        var india = CreateIndiaBoundary();
        var result = PointInPolygon.IsPointInCountry(18.9220, 72.8347, india);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Vancouver_IsInCanada()
    {
        // Vancouver Lookout: 49.2846° N, 123.1116° W (-123.1116)
        var canada = CreateCanadaBoundary();
        var result = PointInPolygon.IsPointInCountry(49.2846, -123.1116, canada);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Sydney_IsInAustralia()
    {
        // Sydney Opera House: 33.8568° S (-33.8568), 151.2153° E
        var australia = CreateAustraliaBoundary();
        var result = PointInPolygon.IsPointInCountry(-33.8568, 151.2153, australia);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Perth_IsInAustralia()
    {
        // Perth Bell Tower: 31.9600° S (-31.9600), 115.8580° E
        var australia = CreateAustraliaBoundary();
        var result = PointInPolygon.IsPointInCountry(-31.9600, 115.8580, australia);
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region Overseas Territories

    [Test]
    public async Task FrenchGuiana_IsInFrance()
    {
        // Cayenne, French Guiana: 4.9224° N, 52.3137° W (-52.3137)
        var france = CreateFranceWithOverseasTerritories();
        var result = PointInPolygon.IsPointInCountry(4.9224, -52.3137, france);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Martinique_IsInFrance()
    {
        // Fort-de-France: 14.6161° N, 61.0588° W (-61.0588)
        var france = CreateFranceWithOverseasTerritories();
        var result = PointInPolygon.IsPointInCountry(14.6161, -61.0588, france);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task PuertoRico_IsInUSA()
    {
        // San Juan, Puerto Rico: 18.4655° N, 66.1057° W (-66.1057)
        var usa = CreateUSAWithTerritories();
        var result = PointInPolygon.IsPointInCountry(18.4655, -66.1057, usa);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Guam_IsInUSA()
    {
        // Hagåtña, Guam: 13.4443° N, 144.7937° E
        var usa = CreateUSAWithTerritories();
        var result = PointInPolygon.IsPointInCountry(13.4443, 144.7937, usa);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task FalklandIslands_IsInUK()
    {
        // Stanley, Falkland Islands: 51.6920° S (-51.6920), 57.8571° W (-57.8571)
        var uk = CreateUKWithOverseasTerritories();
        var result = PointInPolygon.IsPointInCountry(-51.6920, -57.8571, uk);
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region International Waters / Ocean Points

    [Test]
    public async Task MidAtlanticOcean_NotInAnyCountry()
    {
        // Middle of Atlantic: 35.0° N, 40.0° W (-40.0)
        var usa = CreateUSABoundary();
        var uk = CreateUnitedKingdomBoundary();
        var portugal = CreatePortugalBoundary();
        
        await Assert.That(PointInPolygon.IsPointInCountry(35.0, -40.0, usa)).IsFalse();
        await Assert.That(PointInPolygon.IsPointInCountry(35.0, -40.0, uk)).IsFalse();
        await Assert.That(PointInPolygon.IsPointInCountry(35.0, -40.0, portugal)).IsFalse();
    }

    [Test]
    public async Task MidPacificOcean_NotInAnyCountry()
    {
        // Middle of Pacific (far from any land): 10.0° N, 170.0° W (-170.0)
        // This point is well outside Hawaii's bounding box
        var usa = CreateUSAWithHawaiiAccurate();
        var japan = CreateJapanBoundary();
        
        await Assert.That(PointInPolygon.IsPointInCountry(10.0, -170.0, usa)).IsFalse();
        await Assert.That(PointInPolygon.IsPointInCountry(10.0, -170.0, japan)).IsFalse();
    }

    #endregion

    #region Extreme Latitude Points

    [Test]
    public async Task Svalbard_IsInNorway()
    {
        // Longyearbyen, Svalbard: 78.2232° N, 15.6267° E
        var norway = CreateNorwayWithSvalbard();
        var result = PointInPolygon.IsPointInCountry(78.2232, 15.6267, norway);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task UshuaiaArgentina_IsInArgentina()
    {
        // Ushuaia, southernmost city: 54.8019° S (-54.8019), 68.3030° W (-68.3030)
        var argentina = CreateArgentinaBoundary();
        var result = PointInPolygon.IsPointInCountry(-54.8019, -68.3030, argentina);
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region Date Line and Prime Meridian

    [Test]
    public async Task Fiji_MainIslands_InFiji()
    {
        // Suva, Fiji (Viti Levu): 18.1416° S (-18.1416), 178.4419° E
        // Note: This test uses a simplified Fiji boundary that doesn't cross the date line
        // The actual Fiji archipelago spans across 180° longitude, which requires special handling
        var fiji = CreateFijiBoundarySimplified();
        var result = PointInPolygon.IsPointInCountry(-18.1416, 178.4419, fiji);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Greenwich_IsInUK()
    {
        // Royal Observatory, on Prime Meridian: 51.4772° N, 0.0005° W (-0.0005)
        var uk = CreateUnitedKingdomBoundary();
        var result = PointInPolygon.IsPointInCountry(51.4772, -0.0005, uk);
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region European Microstates Cluster

    [Test]
    public async Task EuropeanMicrostates_CorrectlyIdentified()
    {
        // Vatican, Monaco, San Marino, Liechtenstein, Andorra
        var vatican = CreateVaticanBoundary();
        var monaco = CreateMonacoBoundary();
        var sanMarino = CreateSanMarinoBoundary();
        var liechtenstein = CreateLiechtensteinBoundary();
        var andorra = CreateAndorraBoundary();
        
        // Each capital should be in its own country
        await Assert.That(PointInPolygon.IsPointInCountry(41.9022, 12.4539, vatican)).IsTrue();   // Vatican
        await Assert.That(PointInPolygon.IsPointInCountry(43.7396, 7.4281, monaco)).IsTrue();     // Monaco
        await Assert.That(PointInPolygon.IsPointInCountry(43.9333, 12.4500, sanMarino)).IsTrue(); // San Marino
        await Assert.That(PointInPolygon.IsPointInCountry(47.1392, 9.5215, liechtenstein)).IsTrue(); // Liechtenstein
        await Assert.That(PointInPolygon.IsPointInCountry(42.5063, 1.5218, andorra)).IsTrue();    // Andorra
    }

    #endregion

    #region Boundary Creation Helpers - Capital Cities Countries

    private static CountryBoundary CreateFranceBoundary()
    {
        // Metropolitan France (simplified)
        // Approx: 42.3-51.1°N, 4.8°W-8.2°E
        var points = new[]
        {
            new GeoPoint(42.3, -4.8),
            new GeoPoint(51.1, -4.8),
            new GeoPoint(51.1, 8.2),
            new GeoPoint(42.3, 8.2),
            new GeoPoint(42.3, -4.8)
        };
        return new CountryBoundary("FR", "France", new[] { new Polygon(new PolygonRing(points)) }, "FRA");
    }

    private static CountryBoundary CreateUnitedKingdomBoundary()
    {
        // Great Britain + Northern Ireland (simplified)
        // Approx: 49.9-60.9°N, 8.2°W-1.8°E
        var points = new[]
        {
            new GeoPoint(49.9, -8.2),
            new GeoPoint(60.9, -8.2),
            new GeoPoint(60.9, 1.8),
            new GeoPoint(49.9, 1.8),
            new GeoPoint(49.9, -8.2)
        };
        return new CountryBoundary("GB", "United Kingdom", new[] { new Polygon(new PolygonRing(points)) }, "GBR");
    }

    private static CountryBoundary CreateGermanyBoundary()
    {
        // Germany (simplified)
        // Approx: 47.3-55.1°N, 5.9-15.0°E
        var points = new[]
        {
            new GeoPoint(47.3, 5.9),
            new GeoPoint(55.1, 5.9),
            new GeoPoint(55.1, 15.0),
            new GeoPoint(47.3, 15.0),
            new GeoPoint(47.3, 5.9)
        };
        return new CountryBoundary("DE", "Germany", new[] { new Polygon(new PolygonRing(points)) }, "DEU");
    }

    private static CountryBoundary CreateSpainBoundary()
    {
        // Spain (simplified, mainland)
        // Approx: 36.0-43.8°N, 9.3°W-3.3°E
        var points = new[]
        {
            new GeoPoint(36.0, -9.3),
            new GeoPoint(43.8, -9.3),
            new GeoPoint(43.8, 3.3),
            new GeoPoint(36.0, 3.3),
            new GeoPoint(36.0, -9.3)
        };
        return new CountryBoundary("ES", "Spain", new[] { new Polygon(new PolygonRing(points)) }, "ESP");
    }

    private static CountryBoundary CreateItalyBoundary()
    {
        // Italy (simplified, boot shape approximation)
        // Approx: 36.6-47.1°N, 6.6-18.5°E
        var points = new[]
        {
            new GeoPoint(36.6, 6.6),
            new GeoPoint(47.1, 6.6),
            new GeoPoint(47.1, 18.5),
            new GeoPoint(36.6, 18.5),
            new GeoPoint(36.6, 6.6)
        };
        return new CountryBoundary("IT", "Italy", new[] { new Polygon(new PolygonRing(points)) }, "ITA");
    }

    private static CountryBoundary CreateJapanBoundary()
    {
        // Japan (simplified - main islands)
        // Approx: 24.0-46.0°N, 122.0-146.0°E
        var points = new[]
        {
            new GeoPoint(24.0, 122.0),
            new GeoPoint(46.0, 122.0),
            new GeoPoint(46.0, 146.0),
            new GeoPoint(24.0, 146.0),
            new GeoPoint(24.0, 122.0)
        };
        return new CountryBoundary("JP", "Japan", new[] { new Polygon(new PolygonRing(points)) }, "JPN");
    }

    private static CountryBoundary CreateAustraliaBoundary()
    {
        // Australia (simplified)
        // Approx: 44.0°S-10.7°S, 113.0-154.0°E
        var points = new[]
        {
            new GeoPoint(-44.0, 113.0),
            new GeoPoint(-10.7, 113.0),
            new GeoPoint(-10.7, 154.0),
            new GeoPoint(-44.0, 154.0),
            new GeoPoint(-44.0, 113.0)
        };
        return new CountryBoundary("AU", "Australia", new[] { new Polygon(new PolygonRing(points)) }, "AUS");
    }

    private static CountryBoundary CreateUSABoundary()
    {
        // Continental USA (simplified)
        // Approx: 24.5-49.4°N, 125.0-66.9°W
        var points = new[]
        {
            new GeoPoint(24.5, -125.0),
            new GeoPoint(49.4, -125.0),
            new GeoPoint(49.4, -66.9),
            new GeoPoint(24.5, -66.9),
            new GeoPoint(24.5, -125.0)
        };
        return new CountryBoundary("US", "United States", new[] { new Polygon(new PolygonRing(points)) }, "USA");
    }

    private static CountryBoundary CreateCanadaBoundary()
    {
        // Canada (simplified)
        // Approx: 41.7-83.1°N, 141.0-52.6°W
        var points = new[]
        {
            new GeoPoint(41.7, -141.0),
            new GeoPoint(83.1, -141.0),
            new GeoPoint(83.1, -52.6),
            new GeoPoint(41.7, -52.6),
            new GeoPoint(41.7, -141.0)
        };
        return new CountryBoundary("CA", "Canada", new[] { new Polygon(new PolygonRing(points)) }, "CAN");
    }

    private static CountryBoundary CreateRussiaBoundary()
    {
        // Russia (simplified - vast territory)
        // Approx: 41.2-81.9°N, 19.6-169.0°E (and wrapping)
        var points = new[]
        {
            new GeoPoint(41.2, 19.6),
            new GeoPoint(81.9, 19.6),
            new GeoPoint(81.9, 180.0),
            new GeoPoint(41.2, 180.0),
            new GeoPoint(41.2, 19.6)
        };
        return new CountryBoundary("RU", "Russia", new[] { new Polygon(new PolygonRing(points)) }, "RUS");
    }

    private static CountryBoundary CreateChinaBoundary()
    {
        // China (simplified)
        // Approx: 18.2-53.6°N, 73.5-135.0°E
        var points = new[]
        {
            new GeoPoint(18.2, 73.5),
            new GeoPoint(53.6, 73.5),
            new GeoPoint(53.6, 135.0),
            new GeoPoint(18.2, 135.0),
            new GeoPoint(18.2, 73.5)
        };
        return new CountryBoundary("CN", "China", new[] { new Polygon(new PolygonRing(points)) }, "CHN");
    }

    private static CountryBoundary CreateIndiaBoundary()
    {
        // India (simplified)
        // Approx: 6.7-35.5°N, 68.1-97.4°E
        var points = new[]
        {
            new GeoPoint(6.7, 68.1),
            new GeoPoint(35.5, 68.1),
            new GeoPoint(35.5, 97.4),
            new GeoPoint(6.7, 97.4),
            new GeoPoint(6.7, 68.1)
        };
        return new CountryBoundary("IN", "India", new[] { new Polygon(new PolygonRing(points)) }, "IND");
    }

    private static CountryBoundary CreateBrazilBoundary()
    {
        // Brazil (simplified)
        // Approx: 33.8°S-5.3°N, 73.9°W-34.8°W
        var points = new[]
        {
            new GeoPoint(-33.8, -73.9),
            new GeoPoint(5.3, -73.9),
            new GeoPoint(5.3, -34.8),
            new GeoPoint(-33.8, -34.8),
            new GeoPoint(-33.8, -73.9)
        };
        return new CountryBoundary("BR", "Brazil", new[] { new Polygon(new PolygonRing(points)) }, "BRA");
    }

    #endregion

    #region Boundary Creation Helpers - Border Areas

    private static CountryBoundary CreateMexicoBoundary()
    {
        // Mexico (simplified)
        // Approx: 14.5-32.7°N, 118.4-86.7°W
        var points = new[]
        {
            new GeoPoint(14.5, -118.4),
            new GeoPoint(32.7, -118.4),
            new GeoPoint(32.7, -86.7),
            new GeoPoint(14.5, -86.7),
            new GeoPoint(14.5, -118.4)
        };
        return new CountryBoundary("MX", "Mexico", new[] { new Polygon(new PolygonRing(points)) }, "MEX");
    }

    private static CountryBoundary CreateUSASouthernBorder()
    {
        // US near Mexico border (San Diego area)
        var points = new[]
        {
            new GeoPoint(32.54, -117.3),  // Just above Tijuana
            new GeoPoint(35.0, -117.3),
            new GeoPoint(35.0, -114.0),
            new GeoPoint(32.54, -114.0),
            new GeoPoint(32.54, -117.3)
        };
        return new CountryBoundary("US", "United States", new[] { new Polygon(new PolygonRing(points)) }, "USA");
    }

    private static CountryBoundary CreateCanadaSouthernBorder()
    {
        // Canada near Niagara (Ontario)
        var points = new[]
        {
            new GeoPoint(42.0, -80.0),
            new GeoPoint(44.0, -80.0),
            new GeoPoint(44.0, -78.0),
            new GeoPoint(42.0, -78.0),
            new GeoPoint(42.0, -80.0)
        };
        return new CountryBoundary("CA", "Canada", new[] { new Polygon(new PolygonRing(points)) }, "CAN");
    }

    private static CountryBoundary CreateCanadaSouthernBorderNonOverlapping()
    {
        // Canada near Niagara - boundary starts north of the river
        // Niagara Falls Canada is at 43.0896°N, NY side at 43.0962°N
        // Border follows the Niagara River
        var points = new[]
        {
            new GeoPoint(43.10, -80.0),  // Start north of Niagara Falls NY
            new GeoPoint(44.0, -80.0),
            new GeoPoint(44.0, -78.0),
            new GeoPoint(43.10, -78.0),
            new GeoPoint(43.10, -80.0)
        };
        return new CountryBoundary("CA", "Canada", new[] { new Polygon(new PolygonRing(points)) }, "CAN");
    }

    private static CountryBoundary CreateUSANorthernBorder()
    {
        // US near Niagara (New York)
        var points = new[]
        {
            new GeoPoint(42.0, -79.06),  // Border at Niagara River
            new GeoPoint(44.0, -79.06),
            new GeoPoint(44.0, -77.0),
            new GeoPoint(42.0, -77.0),
            new GeoPoint(42.0, -79.06)
        };
        return new CountryBoundary("US", "United States", new[] { new Polygon(new PolygonRing(points)) }, "USA");
    }

    private static CountryBoundary CreateSwitzerlandBoundary()
    {
        // Switzerland (simplified)
        // Approx: 45.8-47.8°N, 5.9-10.5°E
        var points = new[]
        {
            new GeoPoint(45.8, 5.9),
            new GeoPoint(47.8, 5.9),
            new GeoPoint(47.8, 10.5),
            new GeoPoint(45.8, 10.5),
            new GeoPoint(45.8, 5.9)
        };
        return new CountryBoundary("CH", "Switzerland", new[] { new Polygon(new PolygonRing(points)) }, "CHE");
    }

    private static CountryBoundary CreateFranceAlsaceBoundary()
    {
        // France Alsace region (near Strasbourg)
        var points = new[]
        {
            new GeoPoint(47.5, 6.8),
            new GeoPoint(49.0, 6.8),
            new GeoPoint(49.0, 7.8),  // Rhine River as approximate border
            new GeoPoint(47.5, 7.8),
            new GeoPoint(47.5, 6.8)
        };
        return new CountryBoundary("FR", "France", new[] { new Polygon(new PolygonRing(points)) }, "FRA");
    }

    private static CountryBoundary CreateGermanyBadenBoundary()
    {
        // Germany Baden-Württemberg (near Kehl)
        var points = new[]
        {
            new GeoPoint(47.5, 7.8),  // Rhine River as approximate border
            new GeoPoint(49.0, 7.8),
            new GeoPoint(49.0, 10.5),
            new GeoPoint(47.5, 10.5),
            new GeoPoint(47.5, 7.8)
        };
        return new CountryBoundary("DE", "Germany", new[] { new Polygon(new PolygonRing(points)) }, "DEU");
    }

    private static CountryBoundary CreateCanadaWindsorArea()
    {
        // Canada Windsor area (south of Detroit)
        var points = new[]
        {
            new GeoPoint(42.0, -83.5),
            new GeoPoint(42.33, -83.5),  // Detroit River border
            new GeoPoint(42.33, -82.5),
            new GeoPoint(42.0, -82.5),
            new GeoPoint(42.0, -83.5)
        };
        return new CountryBoundary("CA", "Canada", new[] { new Polygon(new PolygonRing(points)) }, "CAN");
    }

    private static CountryBoundary CreateUSADetroitArea()
    {
        // USA Detroit area (north of Windsor)
        var points = new[]
        {
            new GeoPoint(42.32, -83.5),  // Detroit River border
            new GeoPoint(43.0, -83.5),
            new GeoPoint(43.0, -82.5),
            new GeoPoint(42.32, -82.5),
            new GeoPoint(42.32, -83.5)
        };
        return new CountryBoundary("US", "United States", new[] { new Polygon(new PolygonRing(points)) }, "USA");
    }

    #endregion

    #region Boundary Creation Helpers - Enclaves

    private static CountryBoundary CreateItalyWithVaticanHole()
    {
        // Rome area with Vatican as a hole
        var exterior = new PolygonRing(new[]
        {
            new GeoPoint(41.5, 12.0),
            new GeoPoint(42.5, 12.0),
            new GeoPoint(42.5, 13.0),
            new GeoPoint(41.5, 13.0),
            new GeoPoint(41.5, 12.0)
        });
        
        // Vatican City: approx 0.44 sq km
        var vaticanHole = new PolygonRing(new[]
        {
            new GeoPoint(41.900, 12.445),
            new GeoPoint(41.907, 12.445),
            new GeoPoint(41.907, 12.460),
            new GeoPoint(41.900, 12.460),
            new GeoPoint(41.900, 12.445)
        }, isHole: true);
        
        return new CountryBoundary("IT", "Italy", new[] { new Polygon(exterior, new[] { vaticanHole }) }, "ITA");
    }

    private static CountryBoundary CreateVaticanBoundary()
    {
        // Vatican City: ~41.902°N, 12.453°E
        var points = new[]
        {
            new GeoPoint(41.900, 12.445),
            new GeoPoint(41.907, 12.445),
            new GeoPoint(41.907, 12.460),
            new GeoPoint(41.900, 12.460),
            new GeoPoint(41.900, 12.445)
        };
        return new CountryBoundary("VA", "Vatican City", new[] { new Polygon(new PolygonRing(points)) }, "VAT");
    }

    private static CountryBoundary CreateItalyWithSanMarinoHole()
    {
        // Central Italy with San Marino as a hole
        var exterior = new PolygonRing(new[]
        {
            new GeoPoint(43.5, 12.0),
            new GeoPoint(44.5, 12.0),
            new GeoPoint(44.5, 13.0),
            new GeoPoint(43.5, 13.0),
            new GeoPoint(43.5, 12.0)
        });
        
        // San Marino: ~61 sq km
        var sanMarinoHole = new PolygonRing(new[]
        {
            new GeoPoint(43.89, 12.40),
            new GeoPoint(43.99, 12.40),
            new GeoPoint(43.99, 12.52),
            new GeoPoint(43.89, 12.52),
            new GeoPoint(43.89, 12.40)
        }, isHole: true);
        
        return new CountryBoundary("IT", "Italy", new[] { new Polygon(exterior, new[] { sanMarinoHole }) }, "ITA");
    }

    private static CountryBoundary CreateSanMarinoBoundary()
    {
        // San Marino: ~43.94°N, 12.45°E
        var points = new[]
        {
            new GeoPoint(43.89, 12.40),
            new GeoPoint(43.99, 12.40),
            new GeoPoint(43.99, 12.52),
            new GeoPoint(43.89, 12.52),
            new GeoPoint(43.89, 12.40)
        };
        return new CountryBoundary("SM", "San Marino", new[] { new Polygon(new PolygonRing(points)) }, "SMR");
    }

    private static CountryBoundary CreateSouthAfricaWithLesothoHole()
    {
        // South Africa with Lesotho enclave as hole
        var exterior = new PolygonRing(new[]
        {
            new GeoPoint(-35.0, 16.0),
            new GeoPoint(-22.0, 16.0),
            new GeoPoint(-22.0, 33.0),
            new GeoPoint(-35.0, 33.0),
            new GeoPoint(-35.0, 16.0)
        });
        
        // Lesotho: entirely surrounded by South Africa
        var lesothoHole = new PolygonRing(new[]
        {
            new GeoPoint(-30.7, 27.0),
            new GeoPoint(-28.5, 27.0),
            new GeoPoint(-28.5, 29.5),
            new GeoPoint(-30.7, 29.5),
            new GeoPoint(-30.7, 27.0)
        }, isHole: true);
        
        return new CountryBoundary("ZA", "South Africa", new[] { new Polygon(exterior, new[] { lesothoHole }) }, "ZAF");
    }

    private static CountryBoundary CreateLesothoBoundary()
    {
        // Lesotho: ~29.5°S, 28.5°E
        var points = new[]
        {
            new GeoPoint(-30.7, 27.0),
            new GeoPoint(-28.5, 27.0),
            new GeoPoint(-28.5, 29.5),
            new GeoPoint(-30.7, 29.5),
            new GeoPoint(-30.7, 27.0)
        };
        return new CountryBoundary("LS", "Lesotho", new[] { new Polygon(new PolygonRing(points)) }, "LSO");
    }

    private static CountryBoundary CreateFranceWithMonacoHole()
    {
        // French Riviera with Monaco as hole
        var exterior = new PolygonRing(new[]
        {
            new GeoPoint(43.5, 7.0),
            new GeoPoint(44.0, 7.0),
            new GeoPoint(44.0, 7.8),
            new GeoPoint(43.5, 7.8),
            new GeoPoint(43.5, 7.0)
        });
        
        // Monaco: ~2 sq km
        var monacoHole = new PolygonRing(new[]
        {
            new GeoPoint(43.72, 7.41),
            new GeoPoint(43.76, 7.41),
            new GeoPoint(43.76, 7.45),
            new GeoPoint(43.72, 7.45),
            new GeoPoint(43.72, 7.41)
        }, isHole: true);
        
        return new CountryBoundary("FR", "France", new[] { new Polygon(exterior, new[] { monacoHole }) }, "FRA");
    }

    private static CountryBoundary CreateMonacoBoundary()
    {
        // Monaco: 43.7384°N, 7.4246°E
        var points = new[]
        {
            new GeoPoint(43.72, 7.41),
            new GeoPoint(43.76, 7.41),
            new GeoPoint(43.76, 7.45),
            new GeoPoint(43.72, 7.45),
            new GeoPoint(43.72, 7.41)
        };
        return new CountryBoundary("MC", "Monaco", new[] { new Polygon(new PolygonRing(points)) }, "MCO");
    }

    private static CountryBoundary CreateRussiaWithKaliningrad()
    {
        // Russia with Kaliningrad exclave as separate polygon
        var mainland = new[]
        {
            new GeoPoint(41.2, 27.0),
            new GeoPoint(81.9, 27.0),
            new GeoPoint(81.9, 180.0),
            new GeoPoint(41.2, 180.0),
            new GeoPoint(41.2, 27.0)
        };
        
        // Kaliningrad Oblast: ~54.7°N, 20.5°E
        var kaliningrad = new[]
        {
            new GeoPoint(54.3, 19.6),
            new GeoPoint(55.3, 19.6),
            new GeoPoint(55.3, 22.9),
            new GeoPoint(54.3, 22.9),
            new GeoPoint(54.3, 19.6)
        };
        
        return new CountryBoundary("RU", "Russia", new[]
        {
            new Polygon(new PolygonRing(mainland)),
            new Polygon(new PolygonRing(kaliningrad))
        }, "RUS");
    }

    private static CountryBoundary CreatePolandBoundary()
    {
        // Poland (simplified)
        var points = new[]
        {
            new GeoPoint(49.0, 14.1),
            new GeoPoint(54.8, 14.1),
            new GeoPoint(54.8, 24.2),
            new GeoPoint(49.0, 24.2),
            new GeoPoint(49.0, 14.1)
        };
        return new CountryBoundary("PL", "Poland", new[] { new Polygon(new PolygonRing(points)) }, "POL");
    }

    private static CountryBoundary CreatePolandBoundaryAccurate()
    {
        // Poland - accurate northern boundary that doesn't overlap with Kaliningrad
        // Kaliningrad Oblast is at ~54.3-55.3°N, 19.6-22.9°E
        // Poland's border with Kaliningrad is roughly at 54.4°N in that longitude range
        // This creates a boundary that excludes Kaliningrad area
        var points = new[]
        {
            new GeoPoint(49.0, 14.1),
            new GeoPoint(54.8, 14.1),
            new GeoPoint(54.8, 19.5),  // Northern border, west of Kaliningrad
            new GeoPoint(54.35, 19.5), // Drop south to avoid Kaliningrad
            new GeoPoint(54.35, 23.0), // Stay south of Kaliningrad
            new GeoPoint(54.8, 23.0),  // Back up after Kaliningrad
            new GeoPoint(54.8, 24.2),
            new GeoPoint(49.0, 24.2),
            new GeoPoint(49.0, 14.1)
        };
        return new CountryBoundary("PL", "Poland", new[] { new Polygon(new PolygonRing(points)) }, "POL");
    }

    private static CountryBoundary CreateLithuaniaBoundary()
    {
        // Lithuania (simplified)
        var points = new[]
        {
            new GeoPoint(53.9, 21.0),
            new GeoPoint(56.5, 21.0),
            new GeoPoint(56.5, 26.8),
            new GeoPoint(53.9, 26.8),
            new GeoPoint(53.9, 21.0)
        };
        return new CountryBoundary("LT", "Lithuania", new[] { new Polygon(new PolygonRing(points)) }, "LTU");
    }

    #endregion

    #region Boundary Creation Helpers - Islands

    private static CountryBoundary CreateUSAWithHawaii()
    {
        // Continental USA + Hawaii
        var continental = new[]
        {
            new GeoPoint(24.5, -125.0),
            new GeoPoint(49.4, -125.0),
            new GeoPoint(49.4, -66.9),
            new GeoPoint(24.5, -66.9),
            new GeoPoint(24.5, -125.0)
        };
        
        // Hawaii: ~18.9-22.2°N, 160.1-154.8°W
        var hawaii = new[]
        {
            new GeoPoint(18.9, -160.3),
            new GeoPoint(22.3, -160.3),
            new GeoPoint(22.3, -154.8),
            new GeoPoint(18.9, -154.8),
            new GeoPoint(18.9, -160.3)
        };
        
        return new CountryBoundary("US", "United States", new[]
        {
            new Polygon(new PolygonRing(continental)),
            new Polygon(new PolygonRing(hawaii))
        }, "USA");
    }

    private static CountryBoundary CreateUSAWithHawaiiAccurate()
    {
        // Continental USA + Hawaii with tighter Hawaii boundary
        var continental = new[]
        {
            new GeoPoint(24.5, -125.0),
            new GeoPoint(49.4, -125.0),
            new GeoPoint(49.4, -66.9),
            new GeoPoint(24.5, -66.9),
            new GeoPoint(24.5, -125.0)
        };
        
        // Hawaii: actual main islands ~18.9-22.2°N, 160.0-154.8°W
        // Tighter bounding box to avoid including too much ocean
        var hawaii = new[]
        {
            new GeoPoint(18.9, -160.1),
            new GeoPoint(22.3, -160.1),
            new GeoPoint(22.3, -154.8),
            new GeoPoint(18.9, -154.8),
            new GeoPoint(18.9, -160.1)
        };
        
        return new CountryBoundary("US", "United States", new[]
        {
            new Polygon(new PolygonRing(continental)),
            new Polygon(new PolygonRing(hawaii))
        }, "USA");
    }

    private static CountryBoundary CreateNewZealandBoundary()
    {
        // New Zealand: North and South Islands
        var northIsland = new[]
        {
            new GeoPoint(-41.5, 172.5),
            new GeoPoint(-34.4, 172.5),
            new GeoPoint(-34.4, 178.5),
            new GeoPoint(-41.5, 178.5),
            new GeoPoint(-41.5, 172.5)
        };
        
        var southIsland = new[]
        {
            new GeoPoint(-47.3, 166.0),
            new GeoPoint(-40.4, 166.0),
            new GeoPoint(-40.4, 174.5),
            new GeoPoint(-47.3, 174.5),
            new GeoPoint(-47.3, 166.0)
        };
        
        return new CountryBoundary("NZ", "New Zealand", new[]
        {
            new Polygon(new PolygonRing(northIsland)),
            new Polygon(new PolygonRing(southIsland))
        }, "NZL");
    }

    private static CountryBoundary CreateIcelandBoundary()
    {
        // Iceland: ~63.4-66.5°N, 24.5-13.5°W
        var points = new[]
        {
            new GeoPoint(63.4, -24.5),
            new GeoPoint(66.5, -24.5),
            new GeoPoint(66.5, -13.5),
            new GeoPoint(63.4, -13.5),
            new GeoPoint(63.4, -24.5)
        };
        return new CountryBoundary("IS", "Iceland", new[] { new Polygon(new PolygonRing(points)) }, "ISL");
    }

    private static CountryBoundary CreateCubaBoundary()
    {
        // Cuba: ~19.8-23.2°N, 85.0-74.1°W
        var points = new[]
        {
            new GeoPoint(19.8, -85.0),
            new GeoPoint(23.2, -85.0),
            new GeoPoint(23.2, -74.1),
            new GeoPoint(19.8, -74.1),
            new GeoPoint(19.8, -85.0)
        };
        return new CountryBoundary("CU", "Cuba", new[] { new Polygon(new PolygonRing(points)) }, "CUB");
    }

    private static CountryBoundary CreateSingaporeBoundary()
    {
        // Singapore: ~1.15-1.47°N, 103.6-104.0°E
        var points = new[]
        {
            new GeoPoint(1.15, 103.6),
            new GeoPoint(1.47, 103.6),
            new GeoPoint(1.47, 104.1),
            new GeoPoint(1.15, 104.1),
            new GeoPoint(1.15, 103.6)
        };
        return new CountryBoundary("SG", "Singapore", new[] { new Polygon(new PolygonRing(points)) }, "SGP");
    }

    private static CountryBoundary CreatePhilippinesBoundary()
    {
        // Philippines (simplified - main islands)
        var points = new[]
        {
            new GeoPoint(4.5, 116.9),
            new GeoPoint(21.1, 116.9),
            new GeoPoint(21.1, 126.6),
            new GeoPoint(4.5, 126.6),
            new GeoPoint(4.5, 116.9)
        };
        return new CountryBoundary("PH", "Philippines", new[] { new Polygon(new PolygonRing(points)) }, "PHL");
    }

    #endregion

    #region Boundary Creation Helpers - Tiny Countries

    private static CountryBoundary CreateLiechtensteinBoundary()
    {
        // Liechtenstein: ~47.05-47.27°N, 9.47-9.63°E
        var points = new[]
        {
            new GeoPoint(47.05, 9.47),
            new GeoPoint(47.27, 9.47),
            new GeoPoint(47.27, 9.63),
            new GeoPoint(47.05, 9.63),
            new GeoPoint(47.05, 9.47)
        };
        return new CountryBoundary("LI", "Liechtenstein", new[] { new Polygon(new PolygonRing(points)) }, "LIE");
    }

    private static CountryBoundary CreateAndorraBoundary()
    {
        // Andorra: ~42.43-42.65°N, 1.41-1.78°E
        var points = new[]
        {
            new GeoPoint(42.43, 1.41),
            new GeoPoint(42.65, 1.41),
            new GeoPoint(42.65, 1.78),
            new GeoPoint(42.43, 1.78),
            new GeoPoint(42.43, 1.41)
        };
        return new CountryBoundary("AD", "Andorra", new[] { new Polygon(new PolygonRing(points)) }, "AND");
    }

    private static CountryBoundary CreateLuxembourgBoundary()
    {
        // Luxembourg: ~49.45-50.18°N, 5.73-6.53°E
        var points = new[]
        {
            new GeoPoint(49.45, 5.73),
            new GeoPoint(50.18, 5.73),
            new GeoPoint(50.18, 6.53),
            new GeoPoint(49.45, 6.53),
            new GeoPoint(49.45, 5.73)
        };
        return new CountryBoundary("LU", "Luxembourg", new[] { new Polygon(new PolygonRing(points)) }, "LUX");
    }

    private static CountryBoundary CreateMaltaBoundary()
    {
        // Malta: ~35.80-36.08°N, 14.18-14.58°E
        var points = new[]
        {
            new GeoPoint(35.80, 14.18),
            new GeoPoint(36.08, 14.18),
            new GeoPoint(36.08, 14.58),
            new GeoPoint(35.80, 14.58),
            new GeoPoint(35.80, 14.18)
        };
        return new CountryBoundary("MT", "Malta", new[] { new Polygon(new PolygonRing(points)) }, "MLT");
    }

    #endregion

    #region Boundary Creation Helpers - Overseas Territories

    private static CountryBoundary CreateFranceWithOverseasTerritories()
    {
        // Metropolitan France + French Guiana + Martinique
        var metropolitan = new[]
        {
            new GeoPoint(42.3, -4.8),
            new GeoPoint(51.1, -4.8),
            new GeoPoint(51.1, 8.2),
            new GeoPoint(42.3, 8.2),
            new GeoPoint(42.3, -4.8)
        };
        
        // French Guiana: ~2.1-5.8°N, 54.5-51.6°W
        var frenchGuiana = new[]
        {
            new GeoPoint(2.1, -54.5),
            new GeoPoint(5.8, -54.5),
            new GeoPoint(5.8, -51.6),
            new GeoPoint(2.1, -51.6),
            new GeoPoint(2.1, -54.5)
        };
        
        // Martinique: ~14.4-14.9°N, 61.2-60.8°W
        var martinique = new[]
        {
            new GeoPoint(14.4, -61.3),
            new GeoPoint(14.9, -61.3),
            new GeoPoint(14.9, -60.8),
            new GeoPoint(14.4, -60.8),
            new GeoPoint(14.4, -61.3)
        };
        
        return new CountryBoundary("FR", "France", new[]
        {
            new Polygon(new PolygonRing(metropolitan)),
            new Polygon(new PolygonRing(frenchGuiana)),
            new Polygon(new PolygonRing(martinique))
        }, "FRA");
    }

    private static CountryBoundary CreateUSAWithTerritories()
    {
        // Continental USA + Puerto Rico + Guam
        var continental = new[]
        {
            new GeoPoint(24.5, -125.0),
            new GeoPoint(49.4, -125.0),
            new GeoPoint(49.4, -66.9),
            new GeoPoint(24.5, -66.9),
            new GeoPoint(24.5, -125.0)
        };
        
        // Puerto Rico: ~17.9-18.5°N, 67.3-65.2°W
        var puertoRico = new[]
        {
            new GeoPoint(17.9, -67.3),
            new GeoPoint(18.6, -67.3),
            new GeoPoint(18.6, -65.2),
            new GeoPoint(17.9, -65.2),
            new GeoPoint(17.9, -67.3)
        };
        
        // Guam: ~13.2-13.7°N, 144.6-145.0°E
        var guam = new[]
        {
            new GeoPoint(13.2, 144.6),
            new GeoPoint(13.7, 144.6),
            new GeoPoint(13.7, 145.0),
            new GeoPoint(13.2, 145.0),
            new GeoPoint(13.2, 144.6)
        };
        
        return new CountryBoundary("US", "United States", new[]
        {
            new Polygon(new PolygonRing(continental)),
            new Polygon(new PolygonRing(puertoRico)),
            new Polygon(new PolygonRing(guam))
        }, "USA");
    }

    private static CountryBoundary CreateUKWithOverseasTerritories()
    {
        // UK mainland + Falkland Islands
        var mainland = new[]
        {
            new GeoPoint(49.9, -8.2),
            new GeoPoint(60.9, -8.2),
            new GeoPoint(60.9, 1.8),
            new GeoPoint(49.9, 1.8),
            new GeoPoint(49.9, -8.2)
        };
        
        // Falkland Islands: ~52.5-51.0°S, 61.3-57.7°W
        var falklands = new[]
        {
            new GeoPoint(-52.5, -61.3),
            new GeoPoint(-51.0, -61.3),
            new GeoPoint(-51.0, -57.7),
            new GeoPoint(-52.5, -57.7),
            new GeoPoint(-52.5, -61.3)
        };
        
        return new CountryBoundary("GB", "United Kingdom", new[]
        {
            new Polygon(new PolygonRing(mainland)),
            new Polygon(new PolygonRing(falklands))
        }, "GBR");
    }

    private static CountryBoundary CreatePortugalBoundary()
    {
        // Portugal (mainland)
        var points = new[]
        {
            new GeoPoint(36.9, -9.5),
            new GeoPoint(42.2, -9.5),
            new GeoPoint(42.2, -6.2),
            new GeoPoint(36.9, -6.2),
            new GeoPoint(36.9, -9.5)
        };
        return new CountryBoundary("PT", "Portugal", new[] { new Polygon(new PolygonRing(points)) }, "PRT");
    }

    #endregion

    #region Boundary Creation Helpers - Extreme Latitudes

    private static CountryBoundary CreateNorwayWithSvalbard()
    {
        // Norway mainland + Svalbard
        var mainland = new[]
        {
            new GeoPoint(57.9, 4.6),
            new GeoPoint(71.2, 4.6),
            new GeoPoint(71.2, 31.1),
            new GeoPoint(57.9, 31.1),
            new GeoPoint(57.9, 4.6)
        };
        
        // Svalbard: ~76.5-80.8°N, 10.5-28.5°E
        var svalbard = new[]
        {
            new GeoPoint(76.5, 10.5),
            new GeoPoint(80.8, 10.5),
            new GeoPoint(80.8, 28.5),
            new GeoPoint(76.5, 28.5),
            new GeoPoint(76.5, 10.5)
        };
        
        return new CountryBoundary("NO", "Norway", new[]
        {
            new Polygon(new PolygonRing(mainland)),
            new Polygon(new PolygonRing(svalbard))
        }, "NOR");
    }

    private static CountryBoundary CreateArgentinaBoundary()
    {
        // Argentina (simplified, including Tierra del Fuego)
        var points = new[]
        {
            new GeoPoint(-55.1, -73.6),
            new GeoPoint(-21.8, -73.6),
            new GeoPoint(-21.8, -53.6),
            new GeoPoint(-55.1, -53.6),
            new GeoPoint(-55.1, -73.6)
        };
        return new CountryBoundary("AR", "Argentina", new[] { new Polygon(new PolygonRing(points)) }, "ARG");
    }

    private static CountryBoundary CreateFijiBoundary()
    {
        // Fiji (simplified - main islands, crossing date line is complex)
        var points = new[]
        {
            new GeoPoint(-21.0, 177.0),
            new GeoPoint(-12.5, 177.0),
            new GeoPoint(-12.5, -179.0),  // Crosses date line
            new GeoPoint(-21.0, -179.0),
            new GeoPoint(-21.0, 177.0)
        };
        return new CountryBoundary("FJ", "Fiji", new[] { new Polygon(new PolygonRing(points)) }, "FJI");
    }

    private static CountryBoundary CreateFijiBoundarySimplified()
    {
        // Fiji - simplified to just cover the main islands (Viti Levu, Vanua Levu)
        // without crossing the date line (western portion)
        // Main islands are at approximately 177°E to 179°E
        var points = new[]
        {
            new GeoPoint(-21.0, 176.0),
            new GeoPoint(-12.5, 176.0),
            new GeoPoint(-12.5, 180.0),
            new GeoPoint(-21.0, 180.0),
            new GeoPoint(-21.0, 176.0)
        };
        return new CountryBoundary("FJ", "Fiji", new[] { new Polygon(new PolygonRing(points)) }, "FJI");
    }

    #endregion
}
