namespace ByJP.Ror2.Play.Config
{
    /// <summary>
    /// Tag object understood by the BepInEx <c>ConfigurationManager</c> plugin (the
    /// in-game F1 settings menu) via duck-typed reflection over a config entry's
    /// <c>ConfigDescription</c> tags. We only use <see cref="ReadOnly"/> — to render the
    /// live login status as a non-editable line. Ignored (harmless) if ConfigurationManager
    /// isn't installed. Field names/types must match ConfigurationManager's own class.
    /// </summary>
    public sealed class ConfigurationManagerAttributes
    {
        public bool? ReadOnly;
    }
}
