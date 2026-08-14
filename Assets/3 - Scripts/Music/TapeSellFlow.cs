using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Offering a tape to an alien, as a CONVERSATION.
///
/// ── Why this is not a panel ──────────────────────────────────────────────
/// The mushroom sell screen is 1354 lines of bespoke UGUI, and its flow runs
/// the other way round — the buyer prices YOUR goods. This one has the alien
/// ask YOU, which is a dialogue beat, not a spreadsheet. Running it through the
/// choice rows every other NPC already uses means no new screen to lay out, no
/// new input handling, controller support for free, and it reads like talking
/// to someone rather than opening a shop.
///
/// ── The price rows are computed from PUBLIC information ──────────────────
/// The options you get are multiples of the tape's MARKET base — floor plus
/// arrangement — which the player can work out from the console. What they
/// cannot see is this alien's taste, pay factor and patience. So the same row
/// is a comfortable sale to one customer and an insult to the next, and
/// learning who is who IS the game. Offering four rows rather than a free
/// number entry also sidesteps the whole typing-fires-hotkeys problem.
///
/// The caller owns the dialogue text and the choice panel; this drives them
/// through delegates so it can sit on top of any NPC's conversation.
/// </summary>
public class TapeSellFlow
{
    public delegate IEnumerator SpeakFn(string line);
    public delegate IEnumerator AskFn(PostGreetingChoicePanel.Row[] rows);

    readonly SpeakFn _speak;
    readonly AskFn _ask;
    readonly System.Func<int> _choice;
    readonly System.Func<bool> _stillTalking;

    public TapeSellFlow(SpeakFn speak, AskFn ask, System.Func<int> choice,
                        System.Func<bool> stillTalking)
    {
        _speak = speak;
        _ask = ask;
        _choice = choice;
        _stillTalking = stillTalking;
    }

    /// How long they hold the tape before answering. Long enough to actually
    /// hear the loop turn over, short enough not to be a wait.
    public const float ListenSeconds = 6f;

    /// The four asks, as multiples of market base. Named so the rows can say
    /// what kind of move each one is without revealing the alien's number.
    static readonly float[] AskMultipliers = { 0.7f, 1.0f, 1.4f, 2.0f };
    static readonly string[] AskLabels = { "Go low", "Fair price", "Push it", "Chance it" };

    /// <summary>
    /// Run the whole thing. <paramref name="alienId"/> is the stable identity
    /// (AlienIdentity.Of), <paramref name="npcTransform"/> is who plays the tape.
    /// </summary>
    public IEnumerator Run(string alienId, string alienName, Transform npcTransform)
    {
        if (Hotbar.Instance == null) yield break;

        // ── which tape? ──────────────────────────────────────────────────
        var printIds = new List<string>();
        var rows = new List<PostGreetingChoicePanel.Row>();
        for (int i = 0; i < Hotbar.TotalSlots; i++)
        {
            Hotbar.Slot slot = Hotbar.Instance.SlotAt(i);
            if (slot.id != Hotbar.ItemId.Cassette || slot.count <= 0) continue;
            if (string.IsNullOrEmpty(slot.cassetteId)) continue;
            if (printIds.Contains(slot.cassetteId)) continue;

            printIds.Add(slot.cassetteId);
            string tier = TraxPrints.TierOf(slot.cassetteId) >= 2 ? " (Type 2)" : "";
            rows.Add(new PostGreetingChoicePanel.Row(
                TraxPrints.DisplayName(slot.cassetteId) + tier + " x" + slot.count, true));
        }

        if (printIds.Count == 0)
        {
            yield return _speak("You have no tapes on you.");
            yield break;
        }

        rows.Add(new PostGreetingChoicePanel.Row("Never mind.", true));
        yield return _ask(rows.ToArray());
        if (!_stillTalking()) yield break;
        int pick = _choice();
        if (pick < 0 || pick >= printIds.Count) yield break;

        string printId = printIds[pick];
        TraxPrints.Record press = TraxPrints.Get(printId);
        if (press == null) yield break;

        // ── they listen to it, for real ──────────────────────────────────
        yield return _speak(alienName + " takes the tape and puts it on...");
        if (!_stillTalking()) yield break;

        TraxTapePlayer.PlayAt(npcTransform, press.track, ListenSeconds);
        float until = Time.unscaledTime + ListenSeconds;
        while (Time.unscaledTime < until && _stillTalking()) yield return null;
        TraxTapePlayer.StopAll();
        if (!_stillTalking()) yield break;

        double[] dials = DialsOf(press.track);
        uint variant = AlienIdentity.Hash(alienId + printId);

        double satisfaction;
        TapeOffer.Reaction reaction = TapeOffer.Listen(
            alienId, dials, Random.value < 0.5f, out satisfaction);

        // TEMPORARY, while the 9-out-of-9 discrepancy is open. One line per
        // offer, so a normal playtest is also a measurement — the alien's real
        // identity is the piece the headless diagnostic cannot see.
        Debug.Log("[TapeOffer] " + alienName + " [" + alienId + "] likes " +
                  AlienTaste.FavouriteGenre(alienId) +
                  " | tape " + TraxPrints.DisplayName(printId) +
                  " | dist " + AlienTaste.Distance(dials, AlienTaste.TastePoint(alienId)).ToString("0.0") +
                  " | sat " + satisfaction.ToString("0.0") +
                  " | gate " + AlienTaste.Gate(satisfaction) +
                  " -> " + reaction);

        if (reaction == TapeOffer.Reaction.AlreadyHeard)
        {
            yield return _speak(AlienFeedback.ForRepeat(variant));
            TapeMemory.AddBond(alienId, TapeOffer.BondOnRepeatOffer);
            yield break;
        }

        // Heard is recorded whatever they thought of it — they still sat
        // through it, so playing it again later is still playing it again.
        TapeMemory.Remember(alienId, dials);

        if (reaction == TapeOffer.Reaction.Rejected)
        {
            // THEIR favourite genre, not the tape's. Passing the tape's label
            // here made every alien announce they were "more of a DRIFT
            // listener" whenever they were played a DRIFT track — Sam caught it
            // in play, three aliens in a row agreeing about a taste none of
            // them had.
            string genre = AlienTaste.FavouriteGenre(alienId);
            yield return _speak(AlienFeedback.ForRejection(alienId, dials, genre, variant));
            yield break;   // tape stays in the player's hands
        }

        // ── they liked it: name a price ──────────────────────────────────
        yield return _speak(AlienFeedback.ForLiked(satisfaction, variant));
        if (!_stillTalking()) yield break;

        // Did they ORDER this? Matched on the classifier's answer, so the label
        // the computer showed the player is the label the order is judged
        // against — anything else would be marking its own homework.
        bool fillsOrder = TapeRequests.Satisfies(alienId, press.track);
        int value = TapeOffer.Value(alienId, press.track.ActiveCount(), press.tier,
                                    satisfaction, fillsOrder);
        if (fillsOrder)
            yield return _speak("That's the one I was after. Good.");
        if (!_stillTalking()) yield break;
        int marketBase = Mathf.Max(1, Mathf.RoundToInt(
            (float)TapeValue.Base(press.track.ActiveCount(), press.tier)));

        var askRows = new List<PostGreetingChoicePanel.Row>();
        var asks = new List<int>();
        for (int i = 0; i < AskMultipliers.Length; i++)
        {
            int ask = Mathf.Max(1, Mathf.RoundToInt(marketBase * AskMultipliers[i]));
            asks.Add(ask);
            askRows.Add(new PostGreetingChoicePanel.Row(AskLabels[i] + " - $" + ask, true));
        }
        askRows.Add(new PostGreetingChoicePanel.Row("Actually, keep it.", true));

        yield return _ask(askRows.ToArray());
        if (!_stillTalking()) yield break;
        int askPick = _choice();
        if (askPick < 0 || askPick >= asks.Count)
        {
            yield return _speak("Suit yourself.");
            yield break;
        }

        int asked = asks[askPick];
        int counter;
        TapeOffer.Response response = TapeOffer.Judge(alienId, value, asked, out counter);

        if (response == TapeOffer.Response.Accepted)
        {
            yield return Complete(alienId, alienName, printId, value, asked, fillsOrder);
            yield break;
        }

        if (response == TapeOffer.Response.TooLow)
        {
            yield return _speak("Not at that price. " + counter + ", and I'll take it now.");
            if (!_stillTalking()) yield break;
            yield return _ask(new[]
            {
                new PostGreetingChoicePanel.Row("Deal - $" + counter, true),
                new PostGreetingChoicePanel.Row("Forget it.", true),
            });
            if (!_stillTalking()) yield break;
            if (_choice() == 0) yield return Complete(alienId, alienName, printId, value, counter, fillsOrder);
            else yield return _speak("Your loss.");
            yield break;
        }

        // Greed. A take-it-or-leave-it, deliberately below what they would have
        // paid — pushing too hard has to cost something.
        yield return _speak(AlienFeedback.ForFinalOffer(counter, variant));
        if (!_stillTalking()) yield break;
        yield return _ask(new[]
        {
            new PostGreetingChoicePanel.Row("Fine. $" + counter, true),
            new PostGreetingChoicePanel.Row("Not a chance.", true),
        });
        if (!_stillTalking()) yield break;

        if (_choice() == 0)
        {
            yield return Complete(alienId, alienName, printId, value, counter, fillsOrder);
            yield break;
        }

        // They still liked the SONG, so the number is still yours — you just
        // wasted their afternoon getting it.
        TapeMemory.AddBond(alienId, TapeOffer.BondOnRefusedFinal);
        TapeMemory.MakeContact(alienId);
        yield return _speak("Then we're done. ...Here, take my number anyway. I liked the song.");
    }

    /// Money changes hands, the tape leaves, and they become a contact.
    IEnumerator Complete(string alienId, string alienName, string printId, int value, int paid,
                         bool filledOrder)
    {
        Hotbar.Instance.SpendResource(Hotbar.ItemId.Cassette, 1, printId);
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(paid);

        TapeMemory.AddBond(alienId, TapeOffer.BondForSale(value, paid));
        bool newContact = !TapeMemory.IsContact(alienId);
        TapeMemory.MakeContact(alienId);

        // Tev's tapes count toward the lawn, and toward the debt he is owed.
        if (TevDemoTapes.IsTevTape(printId))
        {
            MushroomQuest.SoldCount++;
            MushroomQuest.NotifyTevTapeSold();
        }

        // Only a tape that actually MATCHED the order clears it. Selling them
        // something else is a sale, not a delivery, and quietly cancelling
        // their order for it would lose the player work they had not done yet.
        if (filledOrder) TapeRequests.Fulfil(alienId);

        yield return _speak("Done. $" + paid + ".");
        if (!_stillTalking()) yield break;
        if (newContact)
            yield return _speak("Here - take my number. Bring me something else sometime.");
    }

    /// The six dials as a plain array, which is what the taste model speaks.
    public static double[] DialsOf(TraxTrack track)
    {
        var d = new double[AlienTaste.DialCount];
        if (track == null) return d;
        for (int i = 0; i < d.Length && i < TraxPrng.DialCount; i++) d[i] = track.dials.Get(i);
        return d;
    }
}
