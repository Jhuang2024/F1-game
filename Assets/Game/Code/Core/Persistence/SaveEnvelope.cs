using System;
using System.Collections.Generic;

namespace F1Game.Core.Persistence
{
    /// <summary>
    /// Schema-version probe and migration registry for JSON save files.
    ///
    /// Existing saves were written as raw JSON with no version marker; they are
    /// treated as schema version 0. New writes stamp <c>schemaVersion</c> through
    /// the model itself (models add the field). Migrations are registered per file
    /// family and run on the raw JSON text before deserialization, so a model can
    /// evolve without silently corrupting or discarding an old save.
    /// </summary>
    public static class SaveMigrations
    {
        public delegate string Migration(string json);

        sealed class FamilyPlan
        {
            public readonly SortedDictionary<int, Migration> Steps = new SortedDictionary<int, Migration>();
            public int TargetVersion;
        }

        static readonly Dictionary<string, FamilyPlan> plans = new Dictionary<string, FamilyPlan>();

        /// <summary>Registers the migration that upgrades <paramref name="fileFamily"/> saves from <paramref name="fromVersion"/> to <paramref name="fromVersion"/>+1.</summary>
        public static void Register(string fileFamily, int fromVersion, Migration migration)
        {
            if (!plans.TryGetValue(fileFamily, out FamilyPlan plan))
            {
                plan = new FamilyPlan();
                plans[fileFamily] = plan;
            }

            plan.Steps[fromVersion] = migration;
            plan.TargetVersion = Math.Max(plan.TargetVersion, fromVersion + 1);
        }

        public static string Apply(string fileFamily, string json, out bool migrated)
        {
            migrated = false;
            if (string.IsNullOrEmpty(json) || !plans.TryGetValue(fileFamily, out FamilyPlan plan))
            {
                return json;
            }

            int version = ReadSchemaVersion(json);
            while (version < plan.TargetVersion)
            {
                if (!plan.Steps.TryGetValue(version, out Migration step))
                {
                    break;
                }

                json = step(json);
                version++;
                migrated = true;
            }

            return json;
        }

        /// <summary>
        /// Reads an integer "schemaVersion" property from raw JSON without a full
        /// parse. Absent field (all legacy saves) is version 0.
        /// </summary>
        public static int ReadSchemaVersion(string json)
        {
            const string key = "\"schemaVersion\"";
            int keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return 0;
            }

            int cursor = keyIndex + key.Length;
            while (cursor < json.Length && (json[cursor] == ':' || char.IsWhiteSpace(json[cursor])))
            {
                cursor++;
            }

            int start = cursor;
            while (cursor < json.Length && (char.IsDigit(json[cursor]) || json[cursor] == '-'))
            {
                cursor++;
            }

            return int.TryParse(json.Substring(start, cursor - start), out int version) ? version : 0;
        }

        public static void ClearForTests()
        {
            plans.Clear();
        }
    }
}
