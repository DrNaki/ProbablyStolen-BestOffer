using System.Collections.Generic;

namespace DeepSpaceBestOffer;

public sealed record CategoryIconReference(string Atlas, string Sprite);

// References to native game art; no sample items are created in player saves.
public static class CategoryIconCatalog
{
    public static IReadOnlyDictionary<string, CategoryIconReference> Icons { get; } = new Dictionary<string, CategoryIconReference>
    {
        ["type:FOOD"] = new("Items/items_food", "cat_bar"),
        ["type:MEDICAL"] = new("Items/items_medical", "bandage_small"),
        ["type:MATERIAL"] = new("Items/items_material", "metal_ingot"),
        ["type:HOUSEHOLD_GOOD"] = new("Items/items_amenities", "toilet_paper"),
        ["type:SUBSTANCE"] = new("Items/items_amenities", "cig_red"),
        ["type:WEAPON"] = new("Items/items_weapons_ranged_energy", "glock_receiver"),
        ["type:NOT_FOR_RESALE"] = new("Items/itematlas", "tutorial_book"),
        ["type:SUPPLY_CRATE"] = new("Items/items_container", "sec_box"),
        ["type:TOOL"] = new("Items/items_misc", "flashlight3"),
        ["type:MEDICAL_TOOL"] = new("Items/items_tool", "surgery_toolkit"),
        ["type:RESTRICTED_1"] = new("Items/items_sec", "tazer"),
        ["type:RESTRICTED_2"] = new("Items/items_weapons_ranged_antique", "heavy_smg"),
        ["type:RESTRICTED_3"] = new("Items/itematlas", "c4"),
        ["type:MAKESHIFT"] = new("Items/items_weapons_ranged_energy", "printed_gun"),
        ["type:LOWTECH"] = new("Items/items_weapons_ranged_energy", "handmade_pistol"),
        ["type:MIDLINE"] = new("Items/items_weapons_ranged_energy", "glock_receiver"),
        ["type:HIGHEND"] = new("Items/items_module", "module_black_4"),
        ["type:RARE"] = new("Items/items_medical", "blue_blood_bag"),
        ["type:LUXURY_ITEM"] = new("Items/items_amenities", "perfume1"),
        ["type:ART"] = new("Items/items_amenities", "cassette5"),
        ["type:WATER"] = new("Items/items_food", "water_bottle_full_premium"),
        ["type:WATER_RATION"] = new("Items/items_food", "water_ration"),
        ["type:FARMING_SUPPLIES"] = new("Items/items_hydroponic_plant", "nutrient_tablet_container"),
        ["type:CHEMICAL_SUPPLIES"] = new("Items/items_material", "chemical1"),
        ["type:FRESH_FOOD"] = new("Items/items_hydroponic_plant", "nutrifruit"),
        ["type:PROCESSED_FOOD"] = new("Items/items_food", "processed_meat"),
        ["type:TREAT"] = new("Items/items_food", "beis_icecream"),
        ["type:BREVAGE"] = new("Items/items_food", "items_food_soda_red"),
        ["type:DOCUMENT"] = new("Items/itematlas", "newspaper"),
        ["type:ACCESS_CARD"] = new("Items/items_misc", "sec_keycard"),
        ["type:SEED"] = new("Items/items_hydroponic_plant", "nutrifruit_seed_packet"),
        ["type:BASIC_MATERIAL"] = new("Items/items_material", "scrap_metal"),
        ["type:REFINED_MATERIAL"] = new("Items/items_material", "metal_ingot"),
        ["type:ENERGY_CELL"] = new("Items/items_tool", "battery100"),
        ["type:NARCOTIC"] = new("Items/items_medical", "injector_pink"),
        ["type:POISON"] = new("Items/items_medical", "items_medical_12"),
        ["type:STORAGE"] = new("Items/items_ship", "storage_bay"),
        ["type:WATER_PURIFICATION_SUPPLY"] = new("Items/items_amenities", "water_filter"),
        ["type:OXYGEN_SUPPLY"] = new("Items/items_amenities", "water_filter_adv"),
        ["type:ALCOHOL"] = new("Items/items_food", "nudka"),
        ["type:MELEE_WEAPON"] = new("Items/items_weapons_melee", "combat_knife_2"),
        ["type:FIREARM"] = new("Items/items_weapons_ranged_antique", "heavy_smg"),
        ["type:MODDABLE"] = new("Items/items_weapons_ranged_energy", "glock_receiver"),
        ["type:AMMUNITION"] = new("Items/items_sec", "light_pistol_ammo"),
        ["type:MUNITION"] = new("Items/itematlas", "c4"),
        ["type:GUN_MOD"] = new("Items/items_material", "gun_part"),
        ["type:MODULE"] = new("Items/items_module", "module_white"),
        ["type:NODE"] = new("Items/items_module", "node_s"),
        ["type:HYDROPONIC_MODULE"] = new("Items/items_module", "farm_module_l1"),
        ["type:ALARM_MODULE"] = new("Items/items_module", "farm_module_l7"),
        ["type:PRINTER_MODULE"] = new("Items/items_module", "long_module1"),
        ["type:EVAPORATOR_MODULE"] = new("Items/items_module", "long_module2"),
        ["type:MACHINE"] = new("Items/items_tool", "moisture_farm"),
        // The demo's organ and armor art is still a blank placeholder.
        ["type:ORGAN"] = new("Items/items_medical", "blood_bag"),
        ["type:ARMOR"] = new("Items/items_armor", "kotton_fabric"),
        ["type:FILTER"] = new("Items/items_amenities", "water_filter_adv"),
        ["type:LOTTERY"] = new("Items/items_misc", "scratch"),
        ["type:JUNK"] = new("Items/items_material", "scrap_metal_2"),
    };
}
