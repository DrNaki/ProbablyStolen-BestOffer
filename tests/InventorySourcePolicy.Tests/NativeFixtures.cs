// Native graph fixtures exercise adapter semantics without loading Unity. The
// public surfaces below mirror dump.cs; these tests do not claim runtime proof.
namespace Il2Cpp;

public class NativeObject
{
    private static int nextPointer;
    public IntPtr Pointer { get; } = new(++nextPointer);
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

public class GameInventory : NativeObject
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
