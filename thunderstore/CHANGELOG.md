## 2.0.0
- Now built on the Inscryption API, which is a new dependency. The custom deck is a real starter deck with its own icon on the deck screen, and the "custom deck selected" choice is kept in the API's modded save. The game's own save still never stores the custom deck.
- Card pack support: cards from other mods get their own MODDED tab with their art and sigils. Tested with JSONLoader packs and the Lobotomy Mod.
- Share codes: COPY CODE / PASTE CODE on the DECKS page.
- In-game SETTINGS page for the card limit, copy limit, unlocked-only and boss/special cards.
- Search also finds sigils, a SORT button (cost, attack, health, name), and deck totals under your deck.
- The builder opens from a real Kaycee's Mod menu item on the starter deck screen.
- Much faster drawing: only the visible cards are drawn and card data is read once, which matters with large card packs.
- Hardened against unusual cards from other mods: a card that can't be read is left out, and an unexpected error closes the builder instead of leaving a black screen.
- Fixed names disappearing at the bottom of a scrolled list, and the in-deck count badge covering card names.

## 1.1.1
- Fixed: catch-up cards could still end up in a custom deck run. The game's catch-up rule is now switched off while a custom run is created, so you start with exactly your picks, and cards other mods add at the start of a run are kept.
- Opening the Kaycee's Mod menu no longer freezes for about a second. The mod used to load the game's starter decks early there; the card ID list is now written when you open the builder instead.
- The log now shows how long the mod takes to load and to open the builder.
- Noted in the README that the mod was made with AI assistance.

## 1.1.0
- RANDOM button: 3 random regular cards (at least one costs 1 blood) + 2 Rabbit Pelts.
- Saved decks: name your deck, save it, and load or delete it later from the DECKS list.
- The builder now uses the game's own cursor (with its trail and pointer hand), plays the menu click sound on hover and blinks highlighted buttons like the game's menus.
- Safer save handling: the mod's hooks can no longer interrupt a save, a load or a new run if something goes wrong, and a corrupt save file no longer sends the game into a crash loop.
- Fixed text spilling outside long lists when they scroll.
- Fixed the boss card list in the README.

## 1.0.1
- Fix: card names and stats could show in a tiny fallback font if the game hadn't loaded the card font yet (e.g. with the Inscryption API installed). The font is now loaded directly.
- Leshy no longer plays the "were you expecting more pelts?" punishment lines with a custom deck, since it gets no catch-up cards.
- Screenshots in the README.

## 1.0.0
- First release.
