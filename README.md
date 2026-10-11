# KCM Deck Builder

A BepInEx mod for Inscryption.

Build your own starter deck for **Kaycee's Mod** from inside the game.

A **BUILD CUSTOM DECK** button appears on the *Select Starter Deck* screen. It opens a deck builder styled like the Kaycee's Mod menus, with the real Act 1 card art. Pick your cards, press **START WITH THIS DECK**, and choose your challenges as usual.

The custom deck is a separate option. The game's own starter decks still work exactly as before.

![Deck builder](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/builder-all.png)

## Features

- In-game deck builder with Act 1 card faces: costs, sigils, stats, rare and gold cards.
- Filters: **ALL, PELTS, BOSS, OTHER, BLOOD, BONES, FREE, RARE**, plus search.
- **Random deck.** Press RANDOM for 3 random regular cards (one of them always costs 1 blood) plus 2 Rabbit Pelts. Press again to reroll.
- **Saved decks.** Name and save your decks, then load them again from the DECKS list.
- **Pelts.** Start with Rabbit, Wolf or Golden Pelts and trade them at the first-node Trader.
- **Boss cards** you can't normally get: Bait Bucket, Gold Nugget, Leaping Trap, Pack Mule, Skeleton Crew, Zombie Parrot and Strange Frog.
- **Other cards** you can't normally draft: Boulder, Stump, Dam, Frozen Opossum, The Smoke and more.
- **Your picks only.** No catch-up cards are added with the custom deck (no extra pelts, Opossum or Ring Worm after early losses).
- **First-node Trader.** If your custom deck has pelts, the Trader on the first node is always there.
- **No pointless lecture.** Leshy skips the "were you expecting more pelts?" punishment lines, since the custom deck gets no catch-up cards anyway.
- **Feels like the game.** The game's own cursor with its trail, the pointer hand, menu click sounds and blinking highlights.
- **Unlocks respected.** Only cards you have unlocked are offered (configurable).
- **Same deck on retry.** Restarting after a loss keeps your custom deck.
- **Modded cards.** Nature cards from other mods show up automatically.

## Screenshots

**The BUILD CUSTOM DECK button on the starter deck screen**
![Starter deck screen](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/deck-select.png)

**Hover a card to read its cost and sigils**
![Card info](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/card-info.png)

**Save your decks by name and load them later**
![Saved decks](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/saved-decks.png)

**RANDOM: 3 random cards (one always costs 1 blood) plus 2 Rabbit Pelts**
![Random deck](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/random.png)

**Boss cards you can't normally get**
![Boss cards](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/builder-boss.png)

**Other cards: terrain, Dam, The Smoke and more**
![Other cards](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/builder-other.png)

**Rare cards**
![Rare cards](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/builder-rare.png)

**Your run starts with exactly the cards you picked**
![Run start](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/run-start.png)

**Pelts in your deck? The Trader is waiting on the first node**
![Trader](https://raw.githubusercontent.com/Dutchy-O-o/KCMDeckBuilder/main/docs/trader.png)

## Rules (config)

`BepInEx/config/emre.inscryption.kcmdeckbuilder.cfg`

| Setting | Default | |
|---|---|---|
| `MaxCards` | 5 | Cards you can pick (0 = no limit) |
| `MaxCopiesPerCard` | 0 | Copies of one card (0 = no limit) |
| `OnlyUnlockedCards` | true | Only offer cards you have unlocked |
| `AllowBossAndSpecialCards` | true | Show the BOSS and OTHER cards |

The deck is saved in the same file (`Cards = ...`). A list of valid card IDs is written to `BepInEx/config/KCMDeckBuilder_cards.txt`. Saved decks live in `BepInEx/config/KCMDeckBuilder_decks.txt`.

## Compatibility

- Tested on the Epic version. The Steam version uses the same game code.
- Designed to work alongside the Inscryption API (the custom deck is not added to the starter deck list, so the API can't drop it).
- Your save file never stores the custom deck, so removing the mod is safe; the game falls back to the Vanilla deck.

## Installation

Use r2modman / Thunderstore Mod Manager, or install BepInEx and copy `KCMDeckBuilder.dll` into `BepInEx/plugins`.

## Building

Requires the .NET SDK (6 or newer). Game and BepInEx references come from NuGet, so the game doesn't need to be installed.

```sh
dotnet build -c Release
```

To copy the DLL into your game after building, pass your Inscryption folder:

```sh
dotnet build -c Release -p:GameDir="C:\Path\To\Inscryption"
```

`package.sh` builds the Thunderstore zip into `dist/`.

## License

MIT
