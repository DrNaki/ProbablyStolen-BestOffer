// Native graph fixtures exercise adapter semantics without loading Unity. The
// public surfaces below mirror dump.cs; these tests do not claim runtime proof.
#if !BESTOFFER_WORKSHOP_TEST
namespace Il2Cpp;
#endif

public class NativeObject
{
    private static int nextPointer;
    public IntPtr Pointer { get; } = new(++nextPointer);
    public IntPtr NativeId() => Pointer;
    public T? TryCast<T>() where T : class => this as T;
}

public class GameItem : NativeObject
{
    public string identifier = "";
    public GameInventory? parentInventory;
    public PixelWindow? contentWindow;
    public readonly HashSet<string> Types = new(StringComparer.Ordinal);
    public readonly HashSet<string> Tags = new(StringComparer.Ordinal);
    public int WaterVolume;
    public bool IsGameItemType(string type) => Types.Contains(type);
    public bool IsTag(string tag) => Tags.Contains(tag);
}

public class GraphNodeStorage : NativeObject { }

public class GameInventory : GraphNodeStorage
{
    public GameItem? Owner;
    public GameItem? GetParentItem() => Owner;
}

public class PixelWindow : NativeObject { public NativeObject? childElement; }

public class GridPixelElement : NativeObject
{
    public int gridWidth = 5;
    public int gridHeight = 2;
    public Dictionary<(int, int), NativeObject> Elements = new();
    public NativeObject? GetElement(int x, int y) => Elements.GetValueOrDefault((x, y));
}

public static class WaterHelper { public static bool IsEmpty(GameItem item) => item.WaterVolume == 0; }

// Full-release factories use semantic machinery panels; each panel recursively
// exposes only its physical storage, including intake/stack wrappers. Display
// order is intentionally unrelated to the factory's underlying panel order.
public class MachineryPanelRow : NativeObject
{
    public readonly List<MachineryPanel> Panels = new();
    public MachineryPanel? GetPanel(string role) => Panels.Find(p => p.role == role);
}

public class MachineryPanel : NativeObject
{
    public string role = "";
    public readonly List<GraphNodeStorage> Storage = new();
    public void CollectStorage(List<GraphNodeStorage> list) => list.AddRange(Storage);
}
