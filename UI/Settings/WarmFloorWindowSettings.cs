using System;
using System.IO;
using System.Text.Json;

namespace Underfloor_heating_generator.UI.Settings
{
    internal sealed class WarmFloorWindowSettings
    {
        public bool SaveValues { get; set; }
        public long? PipeTypeId { get; set; }
        public long? SystemTypeId { get; set; }
        public long? DrawingId { get; set; }
        public string PipeDiameter { get; set; }
        public string LayingHeight { get; set; }
        public bool AlignDirectionsToAllowedAngles { get; set; } = true;
    }

    internal static class WarmFloorWindowSettingsStore
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UnderfloorHeatingGenerator", "window-settings.json");

        public static WarmFloorWindowSettings Load()
        {
            try
            {
                return File.Exists(SettingsPath)
                    ? JsonSerializer.Deserialize<WarmFloorWindowSettings>(File.ReadAllText(SettingsPath))
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(WarmFloorWindowSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings,
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
