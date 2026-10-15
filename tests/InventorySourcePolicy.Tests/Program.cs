using DeepSpaceBestOffer;
using Il2Cpp;

int assertions = 0;
void Equal<T>(T expected, T actual, string message)
{
    assertions++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}; got {actual}");
}
void Allowed(GameItem item, TradeOptions options, bool expected, string message) =>
    Equal(expected, NativeInventorySourcePolicy.BoundaryReason(item, options).Length == 0, message);

var root = new GameInventory();
var ordinary = new TradeOptions { IncludeContainerContents = true };
var products = new TradeOptions { IncludeMachineProducts = true };
var machineInputsOnly = new TradeOptions { IncludeMachineContents = true };
var allMachine = new TradeOptions { IncludeMachineContents = true, IncludeMachineProducts = true };
var furnace = new GameItem { identifier = "furnace", parentInventory = root };
furnace.Types.Add("MACHINE");
furnace.Tags.Add("CONTAINER_TAG");
var layout = new GridPixelElement();
furnace.contentWindow = new PixelWindow { childElement = layout };
var battery = new GameInventory { Owner = furnace };
var modules = new GameInventory { Owner = furnace };
var input = new GameInventory { Owner = furnace };
var output = new GameInventory { Owner = furnace };
layout.Elements[(0, 1)] = battery;
layout.Elements[(1, 1)] = modules;
layout.Elements[(2, 1)] = input;
layout.Elements[(3, 1)] = output;
var installedBattery = new GameItem { identifier = "energy_credit", parentInventory = battery };
var installedModule = new GameItem { identifier = "system_module_quality", parentInventory = modules };
// Use the same native commodity identity in both slots: source, not identity,
// distinguishes an unprocessed ingredient from a completed product.
var material = new GameItem { identifier = "metal", parentInventory = input };
var ingot = new GameItem { identifier = "metal", parentInventory = output };
Allowed(furnace, new(), true, "machine body remains sellable");
foreach (var item in new[] { installedBattery, installedModule, material, ingot })
{
    Allowed(item, new(), false, "machine contents default to excluded");
    Allowed(item, ordinary, false, "ordinary storage option cannot remove machine contents");
    Allowed(item, allMachine, true, "explicit machine content opt-in allows every removable slot");
}
Allowed(ingot, products, true, "only native output is allowed by products");
Allowed(ingot, machineInputsOnly, false, "machine option must preserve products when the independent product option is off");
foreach (var item in new[] { installedBattery, installedModule, material })
{
    Allowed(item, machineInputsOnly, true, "machine option independently allows batteries/modules/ingredients");
    Allowed(item, products, false, "product option preserves batteries/modules/ingredients");
}
var alias = ingot; // Reaching an object through another root does not change its physical source.
Allowed(alias, ordinary, false, "inventory alias cannot bypass the source boundary");
layout.Elements[(3, 1)] = new GameInventory { Owner = furnace };
Allowed(ingot, products, false, "a changed native output pointer invalidates the old classification");
layout.Elements[(3, 1)] = output;
material.parentInventory = root;
Allowed(material, new(), true, "manually removed loose ingredients are ordinary stock");

var unknownMachine = new GameItem { identifier = "future_machine", parentInventory = root,
    contentWindow = new PixelWindow { childElement = layout } };
unknownMachine.Types.Add("MACHINE");
var unknownSlot = new GameInventory { Owner = unknownMachine };
var unknownProduct = new GameItem { identifier = "metal", parentInventory = unknownSlot };
Allowed(unknownProduct, products, false, "unverified future layouts cannot masquerade as output");
Allowed(unknownProduct, allMachine, true, "explicit machine contents supports future machines");

foreach (var id in new[] { "furnace", "desequencer", "bottle_printer", "box_dispenser", "moisture_farm" })
{
    var machine = new GameItem { identifier = id, parentInventory = root };
    if (id is not "bottle_printer" and not "box_dispenser") machine.Tags.Add("STANDARD_MACHINE_TAG");
    var grid = new GridPixelElement();
    machine.contentWindow = new PixelWindow { childElement = grid };
    var slot = new GameInventory { Owner = machine };
    var position = id switch { "bottle_printer" or "box_dispenser" => (1, 1), "moisture_farm" => (2, 1), _ => (3, 1) };
    grid.Elements[position] = slot;
    var product = new GameItem { identifier = "finished", parentInventory = slot, WaterVolume = 500 };
    if (id == "moisture_farm") product.Tags.Add("LIQUID_CONTAINER_TAG");
    Allowed(product, products, true, "verified native factory output is selectable");
    if (id == "moisture_farm")
    {
        product.WaterVolume = 0;
        Allowed(product, products, false, "empty collector bottle is production equipment");
        Allowed(product, allMachine, true, "empty collector bottle needs machine content opt-in");
    }
}

var box = new GameItem { identifier = "storage_box", parentInventory = root };
box.Tags.Add("CONTAINER_TAG");
var boxSlot = new GameInventory { Owner = box };
var loose = new GameItem { identifier = "water_tablet", parentInventory = boxSlot };
Allowed(loose, ordinary, true, "ordinary storage still exposes its loose contents");
Allowed(loose, products, false, "products do not implicitly enable ordinary storage");
var package = new GameItem { identifier = "water_tablet_container", parentInventory = boxSlot };
package.Tags.Add("PRE_CONTAINER_TAG");
var tablets = new GameInventory { Owner = package };
var tablet = new GameItem { identifier = "water_tablet", parentInventory = tablets };
Equal(InventoryContainerKind.RetailPackage, NativeInventorySourcePolicy.GetContainerKind(package), "native prepackaged tag");
Allowed(package, ordinary, true, "a full retail package may be removed as one item");
Allowed(tablet, ordinary, false, "ordinary storage opt-in never silently splits packages");
Allowed(tablet, new() { IncludeContainerContents = true, SplitRetailPackages = true }, true, "split-sale explicitly allows packaged contents");
Allowed(tablet, new() { SplitRetailPackages = true }, false, "every outer physical boundary must also be enabled");
var bottle = new GameItem { identifier = "ocutol_pill_bottle", parentInventory = root };
Equal(InventoryContainerKind.RetailPackage, NativeInventorySourcePolicy.GetContainerKind(bottle), "untagged native pharmacy bottle remains packaged");

var weapon = new GameItem { identifier = "firearm", parentInventory = root };
weapon.Types.Add("FIREARM");
weapon.Tags.Add("CONTAINER_TAG");
var magazine = new GameInventory { Owner = weapon };
var ammunition = new GameItem { identifier = "ammo", parentInventory = magazine };
Allowed(weapon, new(), true, "assembled weapon body may sell normally");
Allowed(ammunition, new() { IncludeContainerContents = true, IncludeMachineContents = true,
    IncludeMachineProducts = true, SplitRetailPackages = true }, false, "attachment slots remain protected with all source options on");
var device = new GameItem { identifier = "unclassified_device", parentInventory = root };
var internalSlot = new GameInventory { Owner = device };
Allowed(new() { parentInventory = internalSlot }, ordinary, false, "unclassified devices do not become ordinary storage");

var cycleA = new GameItem();
var cycleB = new GameItem();
cycleA.Tags.Add("CONTAINER_TAG"); cycleB.Tags.Add("CONTAINER_TAG");
cycleA.parentInventory = new() { Owner = cycleB };
cycleB.parentInventory = new() { Owner = cycleA };
Allowed(cycleA, ordinary, false, "malformed cyclic graph cannot authorize extraction");
Allowed(new(), ordinary, false, "detached unknown items are not accessible stock");
Equal(false, InventorySourcePolicy.MayExpand(InventoryContainerKind.None, true, true, true, true), "unrecognized assembled items never expand");
Console.WriteLine($"PASS: {assertions} inventory source assertions (native API graph fixtures; no Unity runtime)");
