// Stub of the save DTO that TapeMemory round-trips through. SaveData.cs pulls
// in UnityEngine, so it cannot be referenced from a headless build — keep this
// field-for-field identical to the real one or this stops testing what ships.

using System.Collections.Generic;

public class TapeMemorySave
{
    public List<string> ids = new List<string>();
    public List<int> bond = new List<int>();
    public List<bool> contact = new List<bool>();
    public List<int> heardCounts = new List<int>();
    public List<float> heardDials = new List<float>();
    // loop-feel D: bought-track lineage (mirrors SaveData.cs)
    public List<int> boughtCounts = new List<int>();
    public List<long> boughtTracks = new List<long>();
}
