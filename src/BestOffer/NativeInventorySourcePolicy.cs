using System;
using System.Collections.Generic;
using Il2Cpp;

namespace DeepSpaceBestOffer;

/// <summary>Read-only native inventory semantics; never derives a source from localized names.</summary>
public static class NativeInventorySourcePolicy
{
    // The current factories for these older pharmaceutical bottles do not all
    // carry PRE_CONTAINER_TAG. Keep their native directory identities explicit.
    private static readonly HashSet<string> PharmaceuticalBottles = new(StringComparer.Ordinal)
    {
        "pill_bottle", "bicarsole_pill_bottle", "ocutol_pill_bottle", "fixalin_pill_bottle",
        "phagimycin_pill_bottle", "unlicensed_phagimycin_pill_bottle", "oxycodone_pill_bottle"
    };

    public static InventoryContainerKind GetContainerKind(GameItem item)
    {
        // Toolbox slots hold frequently used equipment and share the machine opt-in.
        if (item.identifier == "toolbox") return InventoryContainerKind.Machine;
        // The native factories for the fixed bottle/box printers omit MACHINE
        // even though they create dedicated battery and production inventories.
        if (item.IsGameItemType("MACHINE") || item.IsTag("STANDARD_MACHINE_TAG") ||
            item.identifier is "bottle_printer" or "box_dispenser")
            return InventoryContainerKind.Machine;
        if (item.IsTag("PRE_CONTAINER_TAG") || PharmaceuticalBottles.Contains(item.identifier ?? ""))
            return InventoryContainerKind.RetailPackage;
        // A gun can expose storage-shaped magazine/accessory interfaces. Its
        // assembled parts remain with the weapon until the player removes them.
        if (item.IsGameItemType("FIREARM") || item.IsGameItemType("WEAPON") ||
            item.IsTag("ADV_GUN_TAG") || item.IsTag("WEAPON_MOD_TAG"))
            return InventoryContainerKind.None;
        return item.IsTag("CONTAINER_TAG") ? InventoryContainerKind.Storage : InventoryContainerKind.None;
    }

    public static InventoryContentKind GetContentsKind(GameItem owner, GameInventory inventory)
    {
        return GetContainerKind(owner) switch
        {
            InventoryContainerKind.RetailPackage => InventoryContentKind.RetailPackageContent,
            InventoryContainerKind.Storage => InventoryContentKind.StorageContent,
            InventoryContainerKind.Machine => IsOutputInventory(owner, inventory)
                ? InventoryContentKind.MachineProduct : InventoryContentKind.MachineContent,
            _ => InventoryContentKind.AttachedContent
        };
    }

    public static string BoundaryReason(GameItem item, TradeOptions options)
    {
        try
        {
            var seen = new HashSet<IntPtr> { item.Pointer };
            var current = item;
            while (current.parentInventory is { } inventory)
            {
                var owner = inventory.GetParentItem();
                if (owner == null) return "";
                if (!seen.Add(owner.Pointer)) return L.T("容器关联异常，无法安全取出");
                var kind = GetContentsKind(owner, inventory);
                // Liquid output bottles are production receptacles. An empty
                // bottle is equipment waiting for production, not a product.
                if (kind == InventoryContentKind.MachineProduct && IsLiquidOutputReceptacle(owner, inventory) &&
                    (!current.IsTag("LIQUID_CONTAINER_TAG") || WaterHelper.IsEmpty(current)))
                    kind = InventoryContentKind.MachineContent;
                if (!InventorySourcePolicy.CanExtract(kind, options.IncludeContainerContents,
                    options.IncludeMachineContents, options.IncludeMachineProducts, options.SplitRetailPackages))
                    return InventorySourcePolicy.ProtectedReason(kind);
                current = owner;
            }
            return L.T("物品不在可访问库存中");
        }
        catch (Exception) { return L.T("无法可靠读取物品货源层级"); }
    }

    public static bool ProtectsToolboxContents(IEnumerable<GameItem> contents, bool includeMachineContents)
    {
        if (includeMachineContents) return false;
        foreach (var item in contents)
        {
            var current = item;
            var seen = new HashSet<IntPtr> { item.Pointer };
            while (current.parentInventory?.GetParentItem() is { } owner && seen.Add(owner.Pointer))
            {
                if (owner.identifier == "toolbox") return true;
                current = owner;
            }
        }
        return false;
    }

    private static bool IsOutputInventory(GameItem machine, GameInventory inventory)
    {
#if BESTOFFER_NATIVE
        // The full game's machinery UI replaced fixed grids with semantic
        // panels (including wrapped/nested output inventories). Match the real
        // storage identity under the output role, never a translated caption,
        // panel order or an item's type. New standard output panels need no ID
        // whitelist; input/chip/module/power/note panels remain machine contents.
        var panels = machine.contentWindow?.childElement?.TryCast<MachineryPanelRow>();
        if (panels != null)
        {
            if (PanelContains(panels.GetPanel("output"), inventory) ||
                PanelContains(panels.GetPanel("distillate"), inventory)) return true;
            if (machine.identifier == "moisture_farm" && PanelContains(panels.GetPanel("container"), inventory)) return true;
            // DNA manipulation keeps its active seed and disks installed; only
            // the explicit finished-item ejection tray is an output source.
            return machine.IsTag("DNA_MANIPULATOR_TAG") && PanelContains(panels.GetPanel("eject"), inventory);
        }
#endif
        // These are the native factories' physical output positions, confirmed
        // against the legacy GameAssembly. Retain this path for the demo and
        // older grids; never apply these coordinates to the new panel layout.
        // Unknown layouts remain machine contents and require explicit opt-in.
        var position = machine.identifier switch
        {
            "furnace" or "desequencer" => (X: 3, Y: 1),
            "bottle_printer" or "box_dispenser" => (X: 1, Y: 1),
            "moisture_farm" => (X: 2, Y: 1),
            _ => (X: -1, Y: -1)
        };
        if (position.X < 0) return false;
        var grid = machine.contentWindow?.childElement?.TryCast<GridPixelElement>();
        if (grid == null || position.X >= grid.gridWidth || position.Y >= grid.gridHeight) return false;
        var output = grid.GetElement(position.X, position.Y)?.TryCast<GameInventory>();
        return output != null && output.Pointer == inventory.Pointer;
    }

    private static bool IsLiquidOutputReceptacle(GameItem machine, GameInventory inventory)
    {
        if (machine.identifier == "moisture_farm") return true;
#if BESTOFFER_NATIVE
        var panels = machine.contentWindow?.childElement?.TryCast<MachineryPanelRow>();
        return panels != null && PanelContains(panels.GetPanel("distillate"), inventory);
#else
        return false;
#endif
    }

#if BESTOFFER_NATIVE
    private static bool PanelContains(MachineryPanel? panel, GameInventory inventory)
    {
        if (panel == null) return false;
        var storages = new List<GraphNodeStorage>();
        panel.CollectStorage(storages);
        foreach (var storage in storages)
        {
            var candidate = storage.TryCast<GameInventory>();
            if (candidate != null && candidate.Pointer == inventory.Pointer) return true;
        }
        return false;
    }
#endif
}
