# KCM Deck Builder

A BepInEx mod for Inscryption.

Build your own starter deck for **Kaycee's Mod** from inside the game.

A **- BUILD CUSTOM DECK -** menu item appears on the *Select Starter Deck* screen. It opens a deck builder styled like the Kaycee's Mod menus, with the real Act 1 card art. Pick your cards, press **START WITH THIS DECK**, and choose your challenges as usual.

The custom deck is a separate starter deck with its own icon. The game's own starter decks still work exactly as before.

![Deck builder](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/builder-all.png)

## Features

- In-game deck builder with Act 1 card faces: costs, sigils, stats, rare and gold cards.
- Filters: **ALL, PELTS, BOSS, OTHER, BLOOD, BONES, FREE, RARE**, and **MODDED** when card packs are installed.
- **Search by name or sigil.** Type "airborne" to find every card with Airborne.
- **Sort** by cost, attack, health or name. Your deck shows its total blood, bones, attack and health.
- **Card packs.** Cards from other mods (JSONLoader packs, Lobotomy Mod and others) show up with their art and sigils in their own MODDED tab.
- **Share codes.** COPY CODE puts your deck in the clipboard as a short code (`KCM:...`) to post anywhere; PASTE CODE loads one.
- **Saved decks.** Name and save your decks, then load them again from the DECKS list.
- **Random deck.** Press RANDOM for 3 random regular cards (one of them always costs 1 blood) plus 2 Rabbit Pelts. Press again to reroll.
- **In-game settings.** Change the card limit, the copy limit, unlocked-only and boss/special cards from the SETTINGS page.
- **Pelts.** Start with Rabbit, Wolf or Golden Pelts and trade them at the first-node Trader.
- **Boss cards** you can't normally get: Bait Bucket, Gold Nugget, Leaping Trap, Pack Mule, Skeleton Crew, Zombie Parrot and Strange Frog.
- **Other cards** you can't normally draft: Boulder, Stump, Dam, Frozen Opossum, The Smoke and more.
- **Your picks only.** No catch-up cards are added with the custom deck (no extra pelts, Opossum or Ring Worm after early losses). Cards other mods add at the start of a run are kept.
- **First-node Trader.** If your custom deck has pelts, the Trader on the first node is always there.
- **No pointless lecture.** Leshy skips the "were you expecting more pelts?" punishment lines, since the custom deck gets no catch-up cards anyway.
- **Feels like the game.** A real menu item to open it, the game's own cursor with its trail, the pointer hand, menu click sounds and blinking highlights.
- **Unlocks respected.** Only cards you have unlocked are offered (configurable).
- **Same deck on retry.** Restarting after a loss keeps your custom deck.

## Screenshots

**The BUILD CUSTOM DECK menu item on the starter deck screen**
![Starter deck screen](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/deck-select.png)

**Cards from card packs in their own tab**
![Modded cards](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/modded.png)

**Search by sigil, sort by attack or health**
![Search](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/search.png)

**Saved decks and share codes**
![Saved decks](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/saved-decks.png)

**Settings, right in the builder**
![Settings](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/settings.png)

**Your run starts with exactly the cards you picked**
![Run start](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/run-start.png)

**Pelts in your deck? The Trader is waiting on the first node**
![Trader](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/trader.png)

## Rules (config)

Also on the SETTINGS page in the builder. File: `BepInEx/config/emre.inscryption.kcmdeckbuilder.cfg`

| Setting | Default | |
|---|---|---|
| `MaxCards` | 5 | Cards you can pick (0 = no limit) |
| `MaxCopiesPerCard` | 0 | Copies of one card (0 = no limit) |
| `OnlyUnlockedCards` | true | Only offer cards you have unlocked |
| `AllowBossAndSpecialCards` | true | Show the BOSS and OTHER cards |

The deck is saved in the same file (`Cards = ...`). A list of valid card IDs is written to `BepInEx/config/KCMDeckBuilder_cards.txt` when you open the builder. Saved decks live in `BepInEx/config/KCMDeckBuilder_decks.txt`.

## Compatibility

- Requires the **Inscryption API** (installed automatically by mod managers). The custom deck is registered with the API's starter deck manager, like other starter deck mods.
- Your game save never stores the custom deck: the choice is kept in the API's modded save, and the game's save says "Vanilla". Removing the mod is safe.
- Tested with JSONLoader card packs and the Lobotomy Mod. A card from another mod with broken data is left out instead of breaking the builder.
- Tested on the Epic version. The Steam version uses the same game code.

## Installation

Use r2modman / Thunderstore Mod Manager; it installs the Inscryption API too. To install by hand, install BepInEx and the [Inscryption API](https://thunderstore.io/c/inscryption/p/API_dev/API/), then copy `KCMDeckBuilder.dll` into `BepInEx/plugins`.

## Building

Requires the .NET SDK (6 or newer). Game and BepInEx references come from NuGet, and the Inscryption API DLL is downloaded from Thunderstore on the first build, so the game doesn't need to be installed.

```sh
dotnet build -c Release
```

To copy the DLL into your game after building (only done when the API is installed there), pass your Inscryption folder:

```sh
dotnet build -c Release -p:GameDir="C:\Path\To\Inscryption"
```

`package.sh` builds the Thunderstore zip into `dist/`.

## About

Made with AI assistance. Designed, reviewed and tested by me in the game.

## License

MIT
