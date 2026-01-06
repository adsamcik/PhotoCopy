using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhotoCopy.Files;

namespace PhotoCopy.Duplicates;

/// <summary>
/// Detects duplicate files based on content checksums.
/// </summary>
public class DuplicateDetector : IDuplicateDetector
{
    private readonly ILogger<DuplicateDetector> _logger;
    private readonly ConcurrentDictionary<string, IFile> _checksumIndex = new(StringComparer.OrdinalIgnoreCase);

    public DuplicateDetector(ILogger<DuplicateDetector> logger)
    {
        _logger = logger;
    }

    public async Task<DuplicateScanResult> ScanForDuplicatesAsync(
        IEnumerable<IFile> files,
        CancellationToken cancellationToken = default)
    {
        var fileList = files.ToList();
        var duplicateGroups = new Dictionary<string, List<IFile>>(StringComparer.OrdinalIgnoreCase);
        var uniqueFiles = new Dictionary<string, IFile>(StringComparer.OrdinalIgnoreCase);
        var totalScanned = 0;
        var duplicatesFound = 0;

        await Task.Run(() =>
        {
            foreach (var file in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalScanned++;

                var checksum = file.Checksum;
                if (string.IsNullOrEmpty(checksum))
                {
                    _logger.LogDebug("File {File} has no checksum, skipping duplicate detection", file.File.Name);
                    continue;
                }

                if (uniqueFiles.TryGetValue(checksum, out var existing))
                {
                    // This is a duplicate
                    duplicatesFound++;

                    if (!duplicateGroups.TryGetValue(checksum, out var group))
                    {
                        group = new List<IFile> { existing };
                        duplicateGroups[checksum] = group;
                    }

                    group.Add(file);
                    _logger.LogDebug("Found duplicate: {File} matches {Original}",
                        file.File.Name, existing.File.Name);
                }
                else
                {
                    uniqueFiles[checksum] = file;
                    _checksumIndex[checksum] = file;
                }
            }
        }, cancellationToken);

        var groups = duplicateGroups
            .Select(kvp => new DuplicateGroup(kvp.Key, kvp.Value))
            .ToList();

        return new DuplicateScanResult(groups, uniqueFiles, totalScanned, duplicatesFound);
    }

    public IFile? FindDuplicateOf(IFile file)
    {
        var checksum = file.Checksum;
        if (string.IsNullOrEmpty(checksum))
        {
            return null;
        }

        if (_checksumIndex.TryGetValue(checksum, out var existing) &&
            !string.Equals(existing.File.FullName, file.File.FullName, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return null;
    }

    public void RegisterFile(IFile file)
    {
        var checksum = file.Checksum;
        if (!string.IsNullOrEmpty(checksum))
        {
            _checksumIndex.TryAdd(checksum, file);
        }
    }

    public void Clear()
    {
        _checksumIndex.Clear();
    }
}
