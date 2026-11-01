# Best Offer 1.7.2

A native mod for the full release of **Probably Stolen**: smart item selection, inventory protection and one-click trade-table placement. The interface follows the game's language, using Chinese or English. [简体中文](README.md)

## Install and use

- **Steam Workshop:** [Subscribe to Best Offer](https://steamcommunity.com/sharedfiles/filedetails/?id=3830638812), restart, review and enable it in the main-menu Mods screen, then restart as requested.
- **Nexus manual installation:** Exit the game and copy the full-release package's `Mods/BestOffer` folder into the game directory. Enable it in the Mods screen. The native DLL belongs at `Mods/BestOffer/BestOffer.dll`; MelonLoader is not required.
- In your shop, press **F8** or select **Offer Settings** on the trading tablet. Configure Inventory Rules, Category Rules, Offer Plans and Trade Settings. **Auto-fill Offer** places selected goods; review the game's quote and confirm the sale yourself.
- Change hotkeys through the main-menu mod gear or Trade Settings. Both editors stay in sync. Defaults: **F8** for the panel and **F9** for read-only diagnostics while the panel is open. F1–F12 support optional Ctrl, Alt and Shift modifiers.

Compatible game version: **0.46D (100-REV6), Steam build 26337366**. Version 1.7.0 adapts the UI to the full game's trading tablet and reads its categories, including Modified Seed: 61 rule categories including contraband and stolen goods. Version 1.7.1 adds working native mod-menu hotkey settings and fixes the details layout. Version 1.7.2 fixes early language probing during startup, improves text fallbacks and refreshes cached names after language changes. The existing solver, inventory and category policies, budgets and manual settlement behavior are retained.

Preserve `UserData/BestOffer-*.json` when updating. To migrate demo settings, close both games before copying these files into the full game's `UserData`. When local and Workshop copies coexist, the game uses the local copy; check which copy is enabled when updating.

## Demo

![One-click placement](media/v1.7.1/10_English_Auto-Fill-Demo.gif)

[Version 1.7.1 screenshots, videos and GIFs](media/v1.7.1) follow menu order, Chinese first and English second. See the accompanying media notes for original version labels and differences between planned and placed quantities.

## Build

Requires .NET 8 SDK, Python and the installed full game:

```powershell
.\scripts\BuildWorkshop.ps1 -GameDir 'E:\Steam\steamapps\common\Probably Stolen'
```

Output: `workshop-release/1.7.2/content`. The command runs solver, category and hotkey tests; it does not install or upload. Game assemblies are read from your local installation and are not redistributed.

[Native build details (Chinese)](README.Workshop.md) · [Third-party notices](THIRD-PARTY-NOTICES.md)

The legacy MelonLoader demo version remains **1.6.1**, built with `scripts/Build.ps1` into `dist/BestOffer.dll`. The archived instructions below apply to the demo.

<details>
<summary>Demo 1.6.1 instructions</summary>

# Best Offer 1.6.1

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

Press your **panel hotkey**, **Esc**, or **×** to close the panel. If the category picker is open, Esc closes the picker first. Recalculate whenever the buyer, stock, prices, budget or relevant settings change.

In **Trade Settings → Edit Hotkeys**, click **Panel Hotkey** or **Diagnostics Hotkey**, then press F1–F12 with optional Ctrl, Alt and Shift modifiers. Esc cancels capture. Hover over the heading or either button for 500 ms to see instructions; moving away hides them. Defaults are F8 and F9. You can also select Best Offer in Mod Manager, click a hotkey, press the new combination and click **Apply**. Both editors stay in sync and settings persist after restarting. Panel and diagnostics must use different combinations. Diagnostics only exports read-only information while the offer panel is open.

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

</details>
