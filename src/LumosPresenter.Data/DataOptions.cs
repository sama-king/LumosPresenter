namespace LumosPresenter.Data;

/// <summary>Bound from the "Data" configuration section.</summary>
public sealed class DataOptions
{
    public const string SectionName = "Data";

    /// <summary>Path to the single SQLite database (bible + history + future media metadata).</summary>
    public string DatabasePath { get; set; } = "data/lumos.db";

    /// <summary>Directory holding bundled scrollmapper seed files (KJV.db, ASV.db, BSB.db).</summary>
    public string SeedDirectory { get; set; } = "data/seed";

    /// <summary>Translation selected at startup; switchable at runtime from the operator console.</summary>
    public string DefaultTranslation { get; set; } = "KJV";
}
