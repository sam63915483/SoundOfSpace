using System.Collections.Generic;

// Static set of note-IDs the player has read (Tev's intro note, etc.). Notes
// in the world are placed via NotePickup components; on pickup they call
// MarkRead(noteId). Tutorial steps and NPCs check Has(noteId) to gate their
// behavior.
//
// Saved/restored via SaveCollector. Static fields persist across scene reloads.
public static class NoteCollection
{
    static readonly HashSet<string> _read = new HashSet<string>();

    /// True if the player has picked up and read this note.
    public static bool Has(string id) => !string.IsNullOrEmpty(id) && _read.Contains(id);

    /// Marks a note as read. Idempotent — safe to call multiple times.
    public static void MarkRead(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _read.Add(id);
    }

    // ── Save / restore ──

    public static IEnumerable<string> GetReadIds() => _read;

    public static void ApplySaveState(IEnumerable<string> readIds)
    {
        _read.Clear();
        if (readIds != null)
            foreach (var id in readIds)
                if (!string.IsNullOrEmpty(id)) _read.Add(id);
    }
}
