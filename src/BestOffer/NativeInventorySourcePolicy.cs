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
                if (!seen.Add(owner.Pointer)) return "容器关联异常，无法安全取出";
                var kind = GetContentsKind(owner, inventory);
                // The moisture farm's bottle is the output receptacle. An empty
                // bottle is equipment waiting for production, not a water product.
                if (kind == InventoryContentKind.MachineProduct && owner.identifier == "moisture_farm" &&
                    (!current.IsTag("LIQUID_CONTAINER_TAG") || WaterHelper.IsEmpty(current)))
                    kind = InventoryContentKind.MachineContent;
                if (!InventorySourcePolicy.CanExtract(kind, options.IncludeContainerContents,
                    options.IncludeMachineContents, options.IncludeMachineProducts, options.SplitRetailPackages))
                    return InventorySourcePolicy.ProtectedReason(kind);
                current = owner;
            }
            return "物品不在可访问库存中";
        }
        catch (Exception) { return "无法可靠读取物品货源层级"; }
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
        // These are the native factories' physical output positions, confirmed
        // against GameAssembly (not UI labels or arbitrary inventory names).
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
}
