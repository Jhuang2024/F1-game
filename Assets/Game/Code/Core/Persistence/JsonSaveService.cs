using System;
using System.IO;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using UnityEngine;

namespace F1Game.Core.Persistence
{
    /// <summary>
    /// Hardened JSON persistence used by every save path (career, settings,
    /// records, ghosts) via the legacy <c>LocalJsonStore</c> facade.
    ///
    /// Guarantees over the previous direct File.WriteAllText approach:
    ///  - writes go to a temp file first, then replace the target, so a crash
    ///    mid-write can no longer truncate an existing save;
    ///  - the previous good file is rotated to "&lt;name&gt;.bak" before replacement;
    ///  - loads fall back to the backup when the primary is missing or corrupt;
    ///  - registered schema migrations run before deserialization;
    ///  - every save raises <see cref="SaveCompletedEvent"/> so UI can surface
    ///    failures instead of losing progress silently.
    /// </summary>
    public static class JsonSaveService
    {
        public static string RootPath => Application.persistentDataPath;

        public static T Load<T>(string fileName, T fallback)
        {
            string path = Path.Combine(RootPath, fileName);
            if (TryLoadFile(path, fileName, out T primary))
            {
                return primary;
            }

            string backupPath = path + ".bak";
            if (TryLoadFile(backupPath, fileName, out T backup))
            {
                Debug.LogWarning("[Save] Primary file unreadable, recovered from backup: " + fileName);
                return backup;
            }

            return fallback;
        }

        public static bool Save<T>(string fileName, T value)
        {
            string path = Path.Combine(RootPath, fileName);
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(value, true);
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tempPath, path);
                GameEvents.Publish(new SaveCompletedEvent(fileName, true, null));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(DiagnosticLog.FormatError(DiagnosticCode.SaveWriteFailed, "Could not save " + fileName + ": " + exception.Message));
                GameEvents.Publish(new SaveCompletedEvent(fileName, false, exception.Message));
                return false;
            }
        }

        static bool TryLoadFile<T>(string path, string fileFamily, out T value)
        {
            value = default;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                json = SaveMigrations.Apply(fileFamily, json, out bool migrated);
                T parsed = JsonUtility.FromJson<T>(json);
                if (parsed == null)
                {
                    return false;
                }

                if (migrated)
                {
                    Debug.Log("[Save] Migrated " + fileFamily + " to schema version " + SaveMigrations.ReadSchemaVersion(JsonUtility.ToJson(parsed)));
                }

                value = parsed;
                return true;
            }
            catch (Exception exception)
            {
                // A parse/read failure on an existing file reads as corruption -
                // the fallback path (defaults / backup) handles recovery.
                Debug.LogError(DiagnosticLog.FormatError(DiagnosticCode.SaveCorrupt, "Could not load " + path + ": " + exception.Message));
                return false;
            }
        }
    }
}
