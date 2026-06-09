using System;
using BepInEx.Logging;
using ByJP.AtprotoGaming.Core.Adapters;

namespace ByJP.Ror2.Play.Adapters
{
    /// <summary>Routes the package's <see cref="ILogSink"/> to BepInEx's logger.</summary>
    internal sealed class BepInExLogSink : ILogSink
    {
        private readonly ManualLogSource _log;

        public BepInExLogSink(ManualLogSource log) => _log = log;

        public void Info(string message) => _log.LogInfo(message);
        public void Warn(string message) => _log.LogWarning(message);

        public void Error(string message, Exception? exception = null) =>
            _log.LogError(exception is null ? message : message + "\n" + exception);
    }
}
