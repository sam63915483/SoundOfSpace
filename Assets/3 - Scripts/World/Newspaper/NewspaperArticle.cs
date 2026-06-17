using System;
using UnityEngine;

// One readable clipping. Matches the schema in NewspaperArticles_*.json so the
// JSON can seed an asset directly. Paragraphs in `body` are separated by "\n\n".
[Serializable]
public class NewspaperArticle {
    public string id;
    public string headline;
    public string date;        // display only
    [TextArea(3, 14)]
    public string body;        // paragraphs separated by "\n\n"
    public string sourceName;  // link button label, e.g. "Tom's Hardware"
    public string sourceUrl;   // opened directly by the link button

    // Reserved for future journal/collectible tracking (spec §8). Unused for now.
    [NonSerialized] public bool read;
}
