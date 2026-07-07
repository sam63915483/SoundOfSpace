# Ambient Lines — the world notices the story

Additions for `RandomAlienDialogue` (streamed/ambient villagers) and vendor
barks, gated by story flags so the village audibly *keeps up* with the
player's progress. Cheap (strings + a flag check in the line picker) and it
is the difference between "quests happened" and "the world changed."

## Set 0 — always available (foundation flavor, any time)
- "My grandmother said the outer planets used to have SCHEDULES. Imagine."
- "The Eye's brighter than when I was small. Everyone says that. Nobody says it twice."
- "You hum that song too? Everyone hums that song. Nobody remembers learning it."
- "The concerts run every dark now. Some folks never miss one. Some folks REALLY never miss one."

## Set 1 — after `M2_MoonDelivered`
- "Someone lit the moon base again. Saw the windows from the ridge. Felt good. Then it felt strange. Then good again."
- "Alien7 hasn't stopped talking about her preserves. Twelve years, and it's the PRESERVES."
- "My uncle swears the keeper's still up there, walking the far side. My uncle also swears fish can count."

## Set 2 — after `M2_FieryClaims`
- "They're saying the hot-side camp was tidy. Nineteen years and TIDY. I'd have preferred bones, honestly. Bones make sense."
- "Don't take salvage paper to the twins, friend. Some claims are cheap for a reason."

## Set 3 — after `M2_IceyVisited`
- "You went under the ice? And they let you back out? …What do their faces do, when they read?"
- "My cousin traded letters with the ice-folk once. Said the strangest part was how HAPPY they seemed. Quiet, and happy. Like they'd put something heavy down."

## Set 4 — after `M2_PupilReading`
- "Someone told me the Pupil moves. That it tracks. I told them storms don't track. They said 'right, storms don't.' Haven't slept great."
- (looking up, if Cyclops is in the sky:) "It's rude to stare. Somebody should tell IT that."

## Set 5 — after `Interview_Done` (whispered register)
- "Grey coats pulled another faller in for tea, I heard. They only ever ask about the phones. Isn't that odd? The PHONES."
- "My advice? Whatever you carry, carry it like it's nothing. Works on customs, works on the sky."

## Set 6 — Phase 3 active (unease; use sparingly, rate-limit hard)
- "Anyone else feel like the weather's been... attentive? Don't laugh."
- "The dogs won't sleep outside anymore. We don't have dogs. That's how strange it's been."
- "My torch flickered last night with no wind. I said 'I see you too' and it stopped. Try it. Or don't. Don't."

## Vendor barks (one-liners on shop open, flag-gated)
- **Alien7**, after `M2_MoonDelivered`: "Did he ever open my last crates? …No. Don't tell me. Some inventory stays sold."
- **Alien7**, after `M2_ShadowRescue`: "Marlo says you flew a sun into the dark like it was a lunch run. You'll eat free at my stall. Once."
- **Fish vendor (Alien4)**, after `PaleOne_Seen`: "You ate WHAT, WHERE? …Was it rare, at least? Professionally, I have to know if it was rare."
- **Guitar shop**, after `M2_CoverSetDone`: "Whole village heard your set. The whole SKY heard your set. That's the business model, friend."
- **ShipMarket**, after `M2_BeanSalvage`: "That serial you brought in — older than the withdrawal. Older than the REASON for the withdrawal, if you follow me. I don't, and I'm glad."
- **Alien3**, after `CassetteSix_Heard`: "You listened, then. Good. A drawer should get lighter over time. That's how you know it's working."

## Post-ending sets (exclusive per ending flag)
**`Ending_Release`:**
- "Sky feels finished, doesn't it. Like a room after the guest leaves. A good guest. Still a room, though."
- "The wild ones stopped coming out of the dark. Hunters are ANGRY. First peace in living memory and they're angry. People."

**`Ending_Stay`:**
- "The Eye blinked, my aunt says. Once. Deliberate. She's decided it means we're all doing fine. Honestly? Village could use the confidence."
- "Torchlight's been gentler lately. Things stand in it now, at the edge of the fields. They don't come closer. They just… warm up."

**`Ending_Handover`:**
- "The concerts are louder. Or the rest is quieter. One of those."
- "Somebody new is asking questions in the village. Polite ones. Writes everything down. …I liked it better when nobody wrote things down."
