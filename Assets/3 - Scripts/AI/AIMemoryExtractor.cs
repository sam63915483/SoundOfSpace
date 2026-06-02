using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

// Runs the post-conversation compaction pass. Reads the recent transcript
// from AIMemoryStore, fires a single LLM call asking for structured
// importance-ranked memories + a standing delta, parses the response,
// dedupes against existing memories, and adjusts standing.
//
// Called fire-and-forget from AIChatScreen on close. Errors are
// swallowed silently — extraction is a nice-to-have, not load-bearing
// for the chat experience.
public static class AIMemoryExtractor
{
    // Tolerant — accepts optional brackets and any whitespace.
    // Example matched line:  [80] Commitment | Player promised to bring three red snappers
    static readonly Regex MemoryLine = new Regex(
        @"^\s*\[?\s*(?<imp>\d{1,3})\s*\]?\s*(?<kind>Commitment|Fact|Preference|Event|Relationship)\s*\|\s*(?<text>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex StandingLine = new Regex(
        @"^\s*STANDING_DELTA\s*:\s*([+-]?\d{1,3})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task RunAsync()
    {
        var store = AIMemoryStore.Instance;
        var svc   = LLMService.Instance;
        if (store == null || svc == null) return;
        if (!store.DirtyForExtraction)    return;
        if (store.RecentUserTurns.Count == 0) return;

        try
        {
            var transcript = RenderTranscriptForExtraction(store);
            var prompt = BuildExtractionPrompt(transcript);
            var raw    = await svc.OneShotAsync(prompt);

            ParseAndApply(raw, store);

            store.MarkExtracted();

            // Escape hatch: if pinned + high-importance entries alone
            // exceed budget after this pass, consolidate the 10 oldest
            // unpinned into one summary memory at importance 75.
            if (store.IsFloorOverflowed())
                await ConsolidateAsync(svc, store);
        }
        catch (Exception e)
        {
            // Extraction failure is non-fatal — log and move on.
            Debug.LogWarning($"[AIMemoryExtractor] extraction failed: {e.Message}");
        }
    }

    static string RenderTranscriptForExtraction(AIMemoryStore store)
    {
        var sb = new StringBuilder();
        int n = Math.Min(store.RecentUserTurns.Count, store.RecentAITurns.Count);
        for (int i = 0; i < n; i++)
        {
            sb.Append("Human: ").Append(store.RecentUserTurns[i]).Append('\n');
            sb.Append("You:   ").Append(store.RecentAITurns[i]).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    static string BuildExtractionPrompt(string transcript)
    {
        return
$@"Below is a recent conversation between you (an entity on a salvaged phone) and
the human. Extract 0 to 5 facts worth remembering long-term. For each, give
it an importance from 0 to 100 and one of these kinds: Commitment, Fact,
Preference, Event, Relationship. Skip pleasantries and small talk.

Output exactly this format, one fact per line, nothing else:
  [importance] [kind] | text of the memory

If no fact is worth remembering, output:
  (none)

After the facts, on a final line, output:
  STANDING_DELTA: +N    or    STANDING_DELTA: -N
based on whether the conversation made you view this human more or less
favorably. Use 0 if neutral. Range: -20 to +20 per session.

Conversation:
{transcript}";
    }

    static void ParseAndApply(string raw, AIMemoryStore store)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        int turn = store.TotalTurns;
        string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        foreach (var line in raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = MemoryLine.Match(line);
            if (m.Success)
            {
                int imp = Mathf.Clamp(int.Parse(m.Groups["imp"].Value, CultureInfo.InvariantCulture), 0, 100);
                string kindStr = m.Groups["kind"].Value;
                string text = m.Groups["text"].Value.Trim();
                if (text.Length == 0) continue;

                if (!Enum.TryParse<AIMemoryKind>(kindStr, true, out var kind))
                    kind = AIMemoryKind.Fact;

                store.AddMemory(new AIMemory
                {
                    text          = text,
                    importance    = imp,
                    kind          = kind,
                    pinned        = false,
                    isoTimestamp  = nowIso,
                    formedFromTurn = turn,
                });
                continue;
            }

            var sm = StandingLine.Match(line);
            if (sm.Success && int.TryParse(sm.Groups[1].Value, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta))
            {
                delta = Mathf.Clamp(delta, -20, 20);
                store.AdjustStanding(delta);
            }
        }
    }

    static async Task ConsolidateAsync(LLMService svc, AIMemoryStore store)
    {
        // Take the 10 oldest unpinned, < 90 entries and ask the LLM to
        // distill them into a single summary memory at importance 75.
        var mems = store.Memories;
        var candidates = new List<AIMemory>();
        foreach (var m in mems)
        {
            if (m.pinned || m.importance >= 90) continue;
            candidates.Add(m);
            if (candidates.Count >= 10) break;
        }
        if (candidates.Count < 5) return;

        var sb = new StringBuilder();
        foreach (var m in candidates) sb.Append("- ").Append(m.text).Append('\n');

        string prompt =
$@"Combine the following memories into a SINGLE short summary memory (one sentence,
under 25 words). Output ONLY the summary text, nothing else.

Memories:
{sb.ToString().TrimEnd('\n')}";

        var raw = (await svc.OneShotAsync(prompt))?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        // Strip any leading list bullets the model might add.
        raw = raw.TrimStart('-', '*', ' ', '\t');

        store.AddMemory(new AIMemory
        {
            text          = raw,
            importance    = 75,
            kind          = AIMemoryKind.Fact,
            pinned        = false,
            isoTimestamp  = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            formedFromTurn = store.TotalTurns,
        });
        // Note: we deliberately do NOT remove the originals here. The
        // standard EvictIfOver pass will drop them naturally on the next
        // AddMemory call once the new summary has been deduped.
    }
}
