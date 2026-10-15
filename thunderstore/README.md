# KCM Deck Builder

Build your own starter deck for **Kaycee's Mod** from inside the game.

A **- BUILD CUSTOM DECK -** menu item appears on the *Select Starter Deck* screen. It opens a deck builder styled like the Kaycee's Mod menus, with the real Act 1 card art. Pick your cards, press **START WITH THIS DECK**, and choose your challenges as usual.

The custom deck is a separate starter deck with its own icon. The game's own starter decks still work exactly as before.

![Deck builder](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/builder-all.png)

**Watch it in action** (video, 2 minutes)
[![KCM Deck Builder video](https://img.youtube.com/vi/2RcfC0f82Nw/maxresdefault.jpg)](https://www.youtube.com/watch?v=2RcfC0f82Nw)

## Features

- In-game deck builder with Act 1 card faces: costs, sigils, stats, rare and gold cards.
- Filters: **ALL, PELTS, BOSS, OTHER, BLOOD, BONES, FREE, RARE**, and **MODDED** when card packs are installed.
- **Sigils at a glance.** Hover a card to see its sigils with their icons and rulebook text in a box beside it, along with special attack icons such as Ants, Mirror and Bell.
- **Search by name, tribe or sigil.** Type "airborne" to find every card with Airborne, or "bird" for every Bird.
- **Sort** by cost, attack, health or name. Your deck shows its total blood, bones, attack and health.
- **Card packs.** Cards from other mods (JSONLoader packs, Lobotomy Mod and others) show up with their art and sigils in their own MODDED tab. With several mods installed, the **PACK** button shows the cards of one mod at a time (or only the base game's). Tribes, sigils and items from mods (added through the API) can be picked for the totem and the starting items too, and the hover box says which mod they come from.
- **Share codes.** COPY CODE puts your deck, items and totem in the clipboard as a short code (`KCM:...`) to post anywhere; PASTE CODE loads one. Codes from older versions still work.
- **Saved decks.** Name and save your decks, then load them again from the DECKS list.
- **Random deck.** Press RANDOM for 3 random regular cards (one of them always costs 1 blood) plus 2 Rabbit Pelts. Press again to reroll.
- **Starting items.** On the ITEMS page, pick the items your run starts with: up to 3, the same one more than once if you like, or RANDOM. None picked means the game's usual items. The last slot is marked, since the Less Consumables challenge (picked after this screen) takes it away.
- **Starting totem.** On the TOTEM page, pick a head (tribe) and a base (sigil), or press RANDOM. The totem stands on the table from the first battle, and its pieces also go to your inventory, so the Woodcarver lets you rebuild it.
- **Right-click to take a card out** of your deck straight from the card grid.
- **In-game settings.** Change the card limit, the copy limit, unlocked-only and boss/special cards from the SETTINGS page.
- **Pelts.** Start with Rabbit, Wolf or Golden Pelts and trade them at the first-node Trader.
- **Boss cards** you can't normally get: Bait Bucket, Gold Nugget, Leaping Trap, Pack Mule, Skeleton Crew, Zombie Parrot and Strange Frog.
- **Other cards** you can't normally draft: Boulder, Stump, Dam, Frozen Opossum, The Smoke and more.
- **Your picks only.** No catch-up cards are added with the custom deck (no extra pelts, Opossum or Ring Worm after early losses). Cards other mods add at the start of a run are kept. Giving up a custom deck run doesn't count toward those early losses either, so the normal decks are never affected.
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

**One mod at a time with the PACK button**
![Pack filter](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/packs.png)

**Hover a card to read its sigils**
![Sigils](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/sigils.png)

**Special attack icons and tribes too**
![Stat icons](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/stat-icons.png)

**Search by name, tribe or sigil, sort by attack or health**
![Search](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/search.png)

**Saved decks and share codes**
![Saved decks](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/saved-decks.png)

**Pick the items your run starts with**
![Items](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/items.png)

**Start with a totem: pick its head and its base**
![Totem](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/totem.png)

**The totem stands on the table from the first battle, and your items are there too**
![Totem in battle](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/totem-battle.png)

**Its pieces stay with you, so the Woodcarver can rebuild it**
![Woodcarver](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/woodcarver.png)

**Settings, right in the builder**
![Settings](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/settings.png)

**Your run starts with exactly the cards you picked, and challenges still apply (here Annoying Starters adds its sigil to every card)**
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
| `Items` | (empty) | Starting item IDs, e.g. `Pliers, FishHook` (empty = the game's usual items) |
| `Totem` | (empty) | Starting totem as `Tribe:Sigil`, e.g. `Bird:Flying` (empty = none) |

The deck is saved in the same file (`Cards = ...`). A list of valid card IDs is written to `BepInEx/config/KCMDeckBuilder_cards.txt` when you open the builder. Saved decks live in `BepInEx/config/KCMDeckBuilder_decks.txt`.

## Compatibility

- Requires the **Inscryption API** (installed automatically by mod managers). The custom deck is registered with the API's starter deck manager, like other starter deck mods.
- Your game save never stores the custom deck: the choice is kept in the API's modded save, and the game's save says "Vanilla". Removing the mod is safe.
- Tested with JSONLoader card packs and the Lobotomy Mod. A card from another mod with broken data is left out instead of breaking the builder.
- Tested on the Epic version. The Steam version uses the same game code.

## Installation

Use r2modman / Thunderstore Mod Manager; it installs the Inscryption API too. To install by hand, install BepInEx and the [Inscryption API](https://thunderstore.io/c/inscryption/p/API_dev/API/), then copy `KCMDeckBuilder.dll` into `BepInEx/plugins`.

## About

Made with AI assistance. Designed, reviewed and tested by me in the game.
