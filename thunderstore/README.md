# KCM Deck Builder

Build your own starter deck for **Kaycee's Mod** from inside the game.

A **BUILD CUSTOM DECK** button appears on the *Select Starter Deck* screen. It opens a deck builder styled like the Kaycee's Mod menus, with the real Act 1 card art. Pick your cards, press **START WITH THIS DECK**, and choose your challenges as usual.

The custom deck is a separate option. The game's own starter decks still work exactly as before.

## Features

- In-game deck builder with Act 1 card faces: costs, sigils, stats, rare and gold cards.
- Filters: **ALL, PELTS, BOSS, OTHER, BLOOD, BONES, FREE, RARE**, plus search.
- **Pelts.** Start with Rabbit, Wolf or Golden Pelts and trade them at the first-node Trader.
- **Boss cards** you can't normally get: Great White, Bait Bucket, Skeleton Crew, Pack Mule, Strange Frog, Leaping Trap, Urayuli and more.
- **Other cards** you can't normally draft: Boulder, Stump, Dam, Frozen Opossum, The Smoke and more.
- **Your picks only.** No catch-up cards are added with the custom deck (no extra pelts, Opossum or Ring Worm after early losses).
- **First-node Trader.** If your custom deck has pelts, the Trader on the first node is always there.
- **Unlocks respected.** Only cards you have unlocked are offered (configurable).
- **Same deck on retry.** Restarting after a loss keeps your custom deck.
- **Modded cards.** Nature cards from other mods show up automatically.

## Rules (config)

`BepInEx/config/emre.inscryption.kcmdeckbuilder.cfg`

| Setting | Default | |
|---|---|---|
| `MaxCards` | 5 | Cards you can pick (0 = no limit) |
| `MaxCopiesPerCard` | 0 | Copies of one card (0 = no limit) |
| `OnlyUnlockedCards` | true | Only offer cards you have unlocked |
| `AllowBossAndSpecialCards` | true | Show the BOSS and OTHER cards |

The deck is saved in the same file (`Cards = ...`). A list of valid card IDs is written to `BepInEx/config/KCMDeckBuilder_cards.txt`.

## Compatibility

- Tested on the Epic version. The Steam version uses the same game code.
- Designed to work alongside the Inscryption API (the custom deck is not added to the starter deck list, so the API can't drop it).
- Your save file never stores the custom deck, so removing the mod is safe; the game falls back to the Vanilla deck.

## Installation

Use r2modman / Thunderstore Mod Manager, or install BepInEx and copy `KCMDeckBuilder.dll` into `BepInEx/plugins`.
