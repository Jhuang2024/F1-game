using System.IO;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Legacy persistence facade. All call sites (career, settings, records,
    /// time-trial ghosts) keep this API; the implementation now delegates to
    /// <see cref="F1Game.Core.Persistence.JsonSaveService"/>, which adds
    /// atomic temp-file writes, a rotating .bak backup, corruption recovery
    /// from that backup, schema-migration hooks, and SaveCompletedEvent
    /// publication. On-disk format and paths are unchanged, so existing saves
    /// load exactly as before.
    /// </summary>
    public static class LocalJsonStore
    {
        public static T Load<T>(string fileName, T fallback)
        {
            return F1Game.Core.Persistence.JsonSaveService.Load(fileName, fallback);
        }

        public static void Save<T>(string fileName, T value)
        {
            F1Game.Core.Persistence.JsonSaveService.Save(fileName, value);
        }

        public static string GetPath(string fileName)
        {
            return Path.Combine(Application.persistentDataPath, fileName);
        }
    }
}
