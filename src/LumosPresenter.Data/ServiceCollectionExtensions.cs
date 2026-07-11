using LumosPresenter.Core.Abstractions;
using LumosPresenter.Data.Importing;
using LumosPresenter.Data.Migrations;
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
        services.AddSingleton<IVerseRepository, SqliteVerseRepository>();
        services.AddSingleton<IStageRepository, SqliteStageRepository>();
        services.AddSingleton<ISongRepository, SqliteSongRepository>();
        services.AddSingleton<EasyWorshipImporter>();
        return services;
    }
}
