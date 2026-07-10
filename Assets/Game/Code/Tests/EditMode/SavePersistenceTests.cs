using System;
using System.IO;
using F1Game.Core.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace F1Game.Tests
{
    public class SavePersistenceTests
    {
        [Serializable]
        class FakeSave
        {
            public int schemaVersion;
            public string playerName;
            public int season;
        }

        string fileName;

        [SetUp]
        public void SetUp()
        {
            fileName = "f1game_test_save_" + Guid.NewGuid().ToString("N") + ".json";
            SaveMigrations.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
            {
                string path = Path.Combine(Application.persistentDataPath, fileName + suffix);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            SaveMigrations.ClearForTests();
        }

        [Test]
        public void SaveThenLoadRoundTrips()
        {
            var original = new FakeSave { playerName = "Test Driver", season = 3 };
            Assert.IsTrue(JsonSaveService.Save(fileName, original));

            FakeSave loaded = JsonSaveService.Load(fileName, new FakeSave());
            Assert.AreEqual("Test Driver", loaded.playerName);
            Assert.AreEqual(3, loaded.season);
        }

        [Test]
        public void MissingFileReturnsFallback()
        {
            FakeSave fallback = new FakeSave { playerName = "fallback" };
            FakeSave loaded = JsonSaveService.Load("f1game_never_written_" + Guid.NewGuid().ToString("N") + ".json", fallback);
            Assert.AreSame(fallback, loaded);
        }

        [Test]
        public void SecondSaveRotatesABackup()
        {
            JsonSaveService.Save(fileName, new FakeSave { playerName = "first", season = 1 });
            JsonSaveService.Save(fileName, new FakeSave { playerName = "second", season = 2 });

            string backupPath = Path.Combine(Application.persistentDataPath, fileName + ".bak");
            Assert.IsTrue(File.Exists(backupPath), "A .bak of the previous good save must exist");
            StringAssert.Contains("first", File.ReadAllText(backupPath));
        }

        [Test]
        public void CorruptPrimaryRecoversFromBackup()
        {
            JsonSaveService.Save(fileName, new FakeSave { playerName = "good", season = 5 });
            JsonSaveService.Save(fileName, new FakeSave { playerName = "newer", season = 6 });

            // Corrupt the primary (truncated mid-write style).
            string primaryPath = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllText(primaryPath, "{\"playerName\":\"good");

            FakeSave loaded = JsonSaveService.Load(fileName, new FakeSave { playerName = "fallback" });
            Assert.AreEqual("good", loaded.playerName, "Backup content should be recovered");
        }

        [Test]
        public void LegacySaveWithoutSchemaVersionReadsAsVersionZero()
        {
            Assert.AreEqual(0, SaveMigrations.ReadSchemaVersion("{\"playerName\":\"old\"}"));
            Assert.AreEqual(2, SaveMigrations.ReadSchemaVersion("{\"schemaVersion\": 2, \"playerName\":\"x\"}"));
        }

        [Test]
        public void RegisteredMigrationRunsOnLoad()
        {
            // Write a legacy (version 0) payload directly, then register a 0->1
            // migration that renames a value. Load must apply it.
            string primaryPath = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllText(primaryPath, "{\"schemaVersion\":0,\"playerName\":\"legacy name\",\"season\":1}");

            SaveMigrations.Register(fileName, 0, json =>
                json.Replace("\"legacy name\"", "\"migrated name\"").Replace("\"schemaVersion\":0", "\"schemaVersion\":1"));

            FakeSave loaded = JsonSaveService.Load(fileName, new FakeSave());
            Assert.AreEqual("migrated name", loaded.playerName);
            Assert.AreEqual(1, loaded.schemaVersion);
        }

        [Test]
        public void MigrationChainRunsInOrder()
        {
            string json = "{\"schemaVersion\":0,\"season\":1}";
            SaveMigrations.Register("family", 0, j => j.Replace("\"schemaVersion\":0", "\"schemaVersion\":1"));
            SaveMigrations.Register("family", 1, j => j.Replace("\"schemaVersion\":1", "\"schemaVersion\":2").Replace("\"season\":1", "\"season\":99"));

            string result = SaveMigrations.Apply("family", json, out bool migrated);

            Assert.IsTrue(migrated);
            Assert.AreEqual(2, SaveMigrations.ReadSchemaVersion(result));
            StringAssert.Contains("\"season\":99", result);
        }
    }
}
