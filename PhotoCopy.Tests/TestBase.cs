using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests;

public class TestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly ILogger Logger;
    protected readonly FakeLogger GenericLogger;
    protected readonly PhotoCopyConfig DefaultConfig;
    
    public TestBase()
    {
        // Create the generic logger
        GenericLogger = new FakeLogger();
        
        // Set the main logger to our generic logger
        Logger = GenericLogger;
        
        DefaultConfig = new PhotoCopyConfig
        {
            Source = "test-source",
            Destination = "test-destination",
            DryRun = true, // Default to dry-run for tests
            LogLevel = OutputLevel.Verbose, // Verbose for tests
            MinDate = new DateTime(2020, 1, 1), // Add dates for validator tests
            MaxDate = new DateTime(2023, 12, 31)
        };

        var services = new ServiceCollection();
        
        // Add options
        services.AddSingleton<IOptions<PhotoCopyConfig>>(Microsoft.Extensions.Options.Options.Create(DefaultConfig));
        
        // Register our custom loggers
        services.AddSingleton<ILogger>(GenericLogger);
        services.AddSingleton(typeof(ILogger<>), typeof(FakeLogger<>));
        
        // Mock IReverseGeocodingService
        services.AddSingleton(Substitute.For<IReverseGeocodingService>());

        services.AddSingleton<IChecksumCalculator, Sha256ChecksumCalculator>();

        // Register services
        services.AddTransient<IFileMetadataExtractor, FileMetadataExtractor>();
        services.AddTransient<IMetadataEnricher, MetadataEnricher>();
        services.AddTransient<IMetadataEnrichmentStep, DateTimeMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, LocationMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, ChecksumMetadataEnrichmentStep>();
        services.AddTransient<IFileFactory, FileFactory>();
        
        services.AddTransient<IDirectoryScanner, DirectoryScanner>();
        
        services.AddTransient<PhotoCopy.Files.FileSystem>();
        services.AddTransient<IFileSystem>(sp => sp.GetRequiredService<PhotoCopy.Files.FileSystem>());
        
        services.AddTransient<IDirectoryCopier, DirectoryCopier>();
        services.AddTransient<IValidatorFactory, ValidatorFactory>();
        
        // Mock IApplicationState
        services.AddSingleton(Substitute.For<IApplicationState>());

        ServiceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
    
    /// <summary>
    /// Clears all captured log entries
    /// </summary>
    protected void ClearLogEntries()
    {
        SharedLogs.Clear();
    }
    
    /// <summary>
    /// Checks if logs contain a specific text at a specific level
    /// </summary>
    protected bool LogContains(string textToFind, LogLevel level = LogLevel.Information)
    {
        return SharedLogs.Entries.Any(entry => 
            entry.LogLevel == level && 
            entry.Message.Contains(textToFind, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Checks if logs contain an exact text at a specific level
    /// </summary>
    protected bool LogContainsExact(string exactText, LogLevel level = LogLevel.Information)
    {
        return SharedLogs.Entries.Any(entry => 
            entry.LogLevel == level && 
            entry.Message.Equals(exactText, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Returns the count of logs, optionally filtered by level
    /// </summary>
    protected int LogCount(LogLevel? level = null)
    {
        return level.HasValue 
            ? SharedLogs.Entries.Count(entry => entry.LogLevel == level.Value) 
            : SharedLogs.Entries.Count;
    }
    
    /// <summary>
    /// Gets all collected logs
    /// </summary>
    protected IReadOnlyList<LogEntry> GetLogs() => SharedLogs.Entries;
}