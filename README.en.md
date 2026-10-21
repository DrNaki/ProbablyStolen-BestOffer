# Best Offer 1.6.0

Best Offer is a MelonLoader mod for **Probably Stolen Demo**. It finds the highest paying combination of your goods, protects stock you want to keep, and places selected items on the trade table.

The mod follows your **in-game language** automatically. Chinese uses the original Chinese interface; English and every other language use English. Item names, categories and item features come from the game's own English text, including when the game itself is set to another language. Changing the game language refreshes the mod automatically. Existing rules and settings carry over.

## Installation

Requires Windows x64, MelonLoader **0.7.3**, and .NET 6 Runtime x64. Tested game build: **050-Rev5-L / Steam build 26017150**, Unity 2021.3.45f2.

1. Exit the game.
2. Install MelonLoader 0.7.3 if needed. Run the game once to generate its interop assemblies.
3. Copy the package's `Mods` folder into the game folder, so the mod is at `Mods/BestOffer.dll`.
4. Replace the previous DLL when updating. Keep only one enabled copy of Best Offer.



## Using the mod

Press **F8** in your shop, or click **Offer Settings** on the trade tablet. You can configure rules before a buyer arrives.

- **Inventory Rules:** Set copies of a product to **Must Sell** or **Do Not Sell**, by quantity or individually. Check **All** to include both current and future copies in this save. Uncheck it to edit the quantity; the current stock total is retained. Package contents have separate rules for each bottle or box.
- **Category Rules:** Apply a rule to an entire game category. Category rules override inventory rules; **Do Not Sell** wins when categories conflict. Clicking the selected rule clears it. Contraband and stolen goods have fixed shortcuts at the top. Their protection button cycles between Off, Do Not Sell, and **If Requested**. The last option permits only goods the buyer explicitly asks for, subject to inventory rules and buyer restrictions.
- **Offer Plans:** Click **Find Best Offer**, review a plan, then **Place Items**. Check the native quote and accept the sale yourself. **Sell This Plan** provides a separate confirmation before selling.
- **Trade Settings:** Choose inventory sources, market preferences, package splitting, over-budget allowances and hotkeys. Changes save automatically.

**Auto-fill Offer** calculates a plan and places its items on the trade table. You still confirm the sale yourself. Press **Esc** to cancel while it is calculating.

Press **F8**, **Esc**, or **×** to close the panel. If the category picker is open, Esc closes the picker first. Recalculate whenever the buyer, stock, prices, budget or relevant settings change.

## Rules and trading details

Inventory and trade table items are always included. Display cases and hidden storage can be selected separately. **Machines**, including batteries, modules, raw materials and tools inside toolboxes, are excluded by default. **Machine Output** selects finished goods in dedicated output slots and is also off by default.

Packaged consumables are sold whole unless **Sell Packaged Units** is enabled. Protecting any contents protects the whole package when selling it whole. A Must Sell rule for contents requires the whole package unless splitting is enabled.

Today's active calendar events drive **Keep Discounted Items** and **Prioritize Marked-up Items**. Both are off by default. Keeping discounted items takes priority; ordinary goods may fill the remaining budget after marked-up goods.

Buyers who explicitly refuse contraband are never offered contraband or stolen goods, including goods inside containers. Must Sell, If Requested and disabled category protection cannot override this restriction.

**Over-budget Sales** is off by default. Its allowance can be a percentage of the buyer's remaining budget or a fixed number of credits. You give up any item value above the buyer's budget. The sale confirmation shows what you receive and what you forgo.

Ordinary category rules have no expiry by default. Enable **Duration** to set days including today. The remaining days decrease daily; editing them restarts the duration from today. For example, a 3-day rule set on Day 8 applies on Days 8–10 and expires on Day 11. Expired rules revert to inventory rules, and a reminder appears in the nightly report. Category rules are saved separately for each playthrough. Contraband and stolen-goods shortcuts have no expiry.

## Settings, removal and rollback

The game's `UserData` folder contains `BestOffer-settings.json`, `BestOffer-rules.json`, `BestOffer-all-rules.json`, and `BestOffer-category-rules.json`. Keep them when updating. Your existing All rules also work in English, after restarting, and when sold-out items are restocked.

To remove the mod, exit the game and remove `Mods/BestOffer.dll`.

The complete source package includes the verified **1.5.0** release at `rollback/BestOffer-1.5.0.zip`. Exit the game and run `scripts/Rollback.ps1` to restore its DLL and matching PDB. Settings, save files, other mods and the loader are retained.

## Building

With .NET SDK, the game and MelonLoader installed, run `scripts/Build.ps1` from the repository root. Set `-GameDir` if needed; add `-Reproducible` for fixed source paths. The script runs localization and trading tests, then writes `dist/BestOffer.dll`. Building does not install the mod.

Author: **Naki & Codex**. See [third-party notices](THIRD-PARTY-NOTICES.md) for dependency information.
