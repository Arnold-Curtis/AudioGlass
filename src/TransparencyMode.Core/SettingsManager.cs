using Newtonsoft.Json;
using System;
using System.IO;
using TransparencyMode.Core.Models;

namespace TransparencyMode.Core
{
    /// <summary>
    /// Manages application settings persistence
    /// </summary>
    public class SettingsManager
    {
        private static readonly string SettingsDirectory = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TransparencyMode");
        private static readonly string SettingsFile = Path.Combine(SettingsDirectory, "settings.json");

        /// <summary>
        /// Loads settings from disk, or returns defaults if file doesn't exist
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
            }
            catch (Exception)
            {
                // Return defaults on error
            }

            return new AppSettings();
        }

        /// <summary>
        /// Saves settings to disk
        /// </summary>
        public static void Save(AppSettings settings)
        {
            try
            {
                // Create directory if it doesn't exist
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception)
            {
                // Silently fail - settings just won't persist
            }
        }

        /// <summary>
        /// Deletes the settings file
        /// </summary>
        public static void Reset()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    File.Delete(SettingsFile);
                }
            }
            catch (Exception)
            {
                // Silently fail
            }
        }
    }
}
