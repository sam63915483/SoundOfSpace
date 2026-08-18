using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public struct SaveSlotInfo
{
    public string fileName;
    public string displayName;
    public DateTime timestamp;
}

public static class SaveSystem
{
    public static string SavesDir => Path.Combine(Application.persistentDataPath, "saves");

    public static string Save(string saveName)
    {
        try
        {
            return Write(SaveCollector.Capture(saveName), saveName);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Save failed: {e}");
            return null;
        }
    }

    /// <summary>
    /// Write a SaveData that has already been captured.
    ///
    /// Split out for co-op: a guest's pod asks the HOST to capture the world
    /// (only the host's copy is authoritative — its enemies are real bodies
    /// rather than pose puppets, and it owns every timer), receives the finished
    /// SaveData over the wire, and writes it to its own disk under its own slot
    /// name. There is no second serialisation path; both machines end up calling
    /// this.
    /// </summary>
    public static string Write(SaveData data, string saveName)
    {
        if (data == null) return null;
        try
        {
            Directory.CreateDirectory(SavesDir);
            data.saveName = saveName;
            var path = Path.Combine(SavesDir, saveName + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(data, prettyPrint: true));
            Debug.Log($"[SaveSystem] Saved → {path}");
            return path;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Save failed: {e}");
            return null;
        }
    }

    public static SaveData LoadFromDisk(string saveName)
    {
        try
        {
            var path = Path.Combine(SavesDir, saveName + ".json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<SaveData>(json);
            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Load failed: {e}");
            return null;
        }
    }

    public static void Apply(SaveData data)
    {
        // Spawn protection: the restore teleport + settling physics frames can
        // register as a lethal impact (load → instant fall-damage death loop).
        FallDamage.LoadGraceUntil = Time.unscaledTime + 4f;

        // Restore this run's pod-save slot so re-saving (and death respawn)
        // targets the same "stasis pod N" file the loaded run owns. Old saves
        // without the field: infer from the loaded file's own name if it IS a
        // pod slot, else leave for lazy assignment at the next pod save.
        if (!string.IsNullOrEmpty(data.podSlotName))
            StasisPodSave.ActiveSlotName = data.podSlotName;
        else if (!string.IsNullOrEmpty(data.saveName) && data.saveName.StartsWith("stasis pod"))
            StasisPodSave.ActiveSlotName = data.saveName;

        SaveCollector.Apply(data);
    }

    public static List<SaveSlotInfo> ListSaves()
    {
        var list = new List<SaveSlotInfo>();
        if (!Directory.Exists(SavesDir))
        {
            return list;
        }
        var files = Directory.GetFiles(SavesDir, "*.json");
        foreach (var path in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var ts = File.GetLastWriteTime(path);
            list.Add(new SaveSlotInfo
            {
                fileName = name,
                displayName = name + "  ·  " + ts.ToString("yyyy-MM-dd HH:mm"),
                timestamp = ts,
            });
        }
        list.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));
        return list;
    }

    public static void DeleteSave(string saveName)
    {
        var path = Path.Combine(SavesDir, saveName + ".json");
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) { Debug.LogError($"[SaveSystem] Delete failed: {e}"); }
    }

    public static string GenerateName() =>
        "save_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
}
