using System;

namespace DeepSpaceBestOffer;

public enum InventoryContainerKind { None, Storage, Machine, RetailPackage }
public enum InventoryContentKind { AttachedContent, StorageContent, MachineContent, MachineProduct, RetailPackageContent }

/// <summary>Selection permissions for physical inventory boundaries, independent of native game objects.</summary>
public static class InventorySourcePolicy
{
    public static bool CanExtract(InventoryContentKind kind, bool includeContainerContents,
        bool includeMachineContents, bool includeMachineProducts, bool splitRetailPackages) => kind switch
    {
        InventoryContentKind.StorageContent => includeContainerContents,
        InventoryContentKind.MachineContent => includeMachineContents,
        InventoryContentKind.MachineProduct => includeMachineProducts,
        InventoryContentKind.RetailPackageContent => splitRetailPackages,
        _ => false
    };

    public static bool MayExpand(InventoryContainerKind kind, bool includeContainerContents,
        bool includeMachineContents, bool includeMachineProducts, bool splitRetailPackages) => kind switch
    {
        InventoryContainerKind.Storage => includeContainerContents,
        InventoryContainerKind.Machine => includeMachineContents || includeMachineProducts,
        InventoryContainerKind.RetailPackage => splitRetailPackages,
        _ => false
    };

    public static string ProtectedReason(InventoryContentKind kind) => kind switch
    {
        InventoryContentKind.StorageContent => L.T("未启用储藏箱内容货源"),
        InventoryContentKind.MachineContent => L.T("未启用机器内容货源"),
        InventoryContentKind.MachineProduct => L.T("未启用机器产物货源"),
        InventoryContentKind.RetailPackageContent => L.T("整盒／整瓶出售：未启用拆售"),
        _ => L.T("装配部件不作为独立货源")
    };
}
