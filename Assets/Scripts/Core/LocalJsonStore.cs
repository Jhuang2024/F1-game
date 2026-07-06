using System.IO;
using UnityEngine;

namespace LocalFormulaRacing
{
    public static class LocalJsonStore
    {
        public static T Load<T>(string fileName, T fallback)
        {
            string path = GetPath(fileName);
            if (!File.Exists(path))
            {
                return fallback;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return fallback;
                }

                T value = JsonUtility.FromJson<T>(json);
                return value == null ? fallback : value;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Could not load " + fileName + ": " + exception.Message);
                return fallback;
            }
        }

        public static void Save<T>(string fileName, T value)
        {
            try
            {
                string path = GetPath(fileName);
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(value, true);
                File.WriteAllText(path, json);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Could not save " + fileName + ": " + exception.Message);
            }
        }

        public static string GetPath(string fileName)
        {
            return Path.Combine(Application.persistentDataPath, fileName);
        }
    }
}

