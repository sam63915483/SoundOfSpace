using System.Collections.Generic;
using UnityEngine;

// A stack of clippings shown by one table prop. Each table references its own
// asset, so adding a future table = new asset + new prop, zero new code.
[CreateAssetMenu(fileName = "NewspaperArticleSet", menuName = "Sound of Space/Newspaper Article Set")]
public class NewspaperArticleSet : ScriptableObject {
    public string setId;
    public string setTitle;   // optional flavor title for the reader header
    public List<NewspaperArticle> articles = new List<NewspaperArticle>();
}
