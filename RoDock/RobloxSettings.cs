using System.Xml.Linq;

namespace RoDock;

/// Roblox rewrites its settings file (graphics level, FPS cap, volume...) whenever a client starts, so what you set in-game doesn't
/// stick. This keeps a saved copy of that file and puts it back right before every launch.
public class RobloxSettings(string robloxFile, string savedFile)
{
    public static readonly RobloxSettings Default = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "GlobalBasicSettings_13.xml"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoDock", "roblox-settings.xml"));

    public bool HasSaved => File.Exists(savedFile);

    /// Roblox's own file as it was before RoDock first replaced it, kept for a manual restore.
    string Original => savedFile + ".original";

    static bool IsRobloxSettings(string path)
    {
        try { return XDocument.Load(path).Root?.Name.LocalName == "roblox"; }
        catch (System.Xml.XmlException) { return false; }
    }

    /// Keeps the current Roblox settings. Roblox writes the file when a client closes, so close the window after changing settings.
    public void SaveCurrent()
    {
        if (!File.Exists(robloxFile)) throw new FileNotFoundException("Roblox hasn't saved any settings yet. Open Roblox, change what you want, close it, then try again.");
        if (!IsRobloxSettings(robloxFile)) throw new InvalidDataException("Roblox's settings file looks damaged, so it was not saved.");
        Directory.CreateDirectory(Path.GetDirectoryName(savedFile)!);
        File.Copy(robloxFile, savedFile, overwrite: true);
    }

    /// Stops applying; Roblox's own file is left as it is.
    public void Clear() => File.Delete(savedFile);

    /// Puts the saved settings back. False if nothing is saved or the saved copy is damaged (Roblox's file is then left alone).
    public bool Apply()
    {
        if (!HasSaved || !IsRobloxSettings(savedFile)) return false;
        var exists = File.Exists(robloxFile);
        if (exists && !File.Exists(Original)) File.Copy(robloxFile, Original);
        if (!exists) Directory.CreateDirectory(Path.GetDirectoryName(robloxFile)!);
        else if (File.ReadAllBytes(robloxFile).AsSpan().SequenceEqual(File.ReadAllBytes(savedFile))) return true; // already in place
        File.Copy(savedFile, robloxFile, overwrite: true);
        return true;
    }
}
