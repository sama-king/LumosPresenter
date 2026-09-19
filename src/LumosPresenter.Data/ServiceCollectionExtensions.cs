using LumosPresenter.Core.Abstractions;
using LumosPresenter.Data.Importing;
using LumosPresenter.Data.Migrations;
using LumosPresenter.Data.Remote;
using LumosPresenter.Data.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LumosPresenter.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite data layer: migrations, bundled-translation seeding, and the
    /// Dapper verse repository. Call <see cref="DatabaseInitializer.Initialize"/> at startup.
    /// </summary>
    public static IServiceCollection AddLumosData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DataOptions>(configuration.GetSection(DataOptions.SectionName));
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<ScrollmapperImporter>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<SqliteVerseRepository>();

        // Remote translations (api.bible). The caching decorator wraps the local
        // repository, so every existing caller — verse lookup, chapter preview, the
        // translation re-push — transparently gains fetch-on-miss and prefetch.
        services.Configure<ApiBibleOptions>(configuration.GetSection(ApiBibleOptions.SectionName));
        // The api.bible key is the operator's, not the build's, so it lives in the database
        // alongside their data rather than in configuration.
        services.AddSingleton<IAppSettings, SqliteAppSettings>();
        services.AddSingleton<RemoteChapterCache>();
        services.AddHttpClient<IRemoteScriptureSource, ApiBibleScriptureSource>(
            client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IVerseRepository, CachingVerseRepository>();
        services.AddHostedService<CachePurgeService>();
        services.AddSingleton<IStageRepository, SqliteStageRepository>();
        // Runs after DatabaseInitializer (which Program.cs calls before the host starts), so
        // the media_assets table exists by the time the shipped backgrounds are registered.
        services.AddHostedService<BundledBackgroundSeeder>();
        services.AddSingleton<ISongRepository, SqliteSongRepository>();
        services.AddSingleton<IMediaLibraryRepository, SqliteMediaLibraryRepository>();
        services.AddSingleton<EasyWorshipImporter>();
        return services;
    }
}
