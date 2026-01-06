using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Files;

namespace PhotoCopy.Abstractions;

public interface IReverseGeocodingService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    LocationData? ReverseGeocode(double latitude, double longitude);
}
