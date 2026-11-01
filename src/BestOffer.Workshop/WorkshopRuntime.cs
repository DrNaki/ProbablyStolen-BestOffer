using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace DeepSpaceBestOffer
{
    internal static class WorkshopRuntime
    {
        internal static string UserDataDirectory => Path.Combine(Application.dataPath, "..", "UserData");
        internal static void ReplaceFile(string temporary, string destination)
        {
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
    }

    internal static class MonoIdentity
    {
        sealed class Identity { internal readonly IntPtr Value = new(Interlocked.Increment(ref next)); }
        static long next;
        static readonly ConditionalWeakTable<object, Identity> identities = new();
        internal static IntPtr NativeId(this object value) => identities.GetValue(value, _ => new Identity()).Value;
        internal static T? TryCast<T>(this object? value) where T : class => value as T;
    }

    internal sealed class WorkshopJsonOptions { public bool WriteIndented { get; set; } }
    internal static class WorkshopJson
    {
        internal static T? Deserialize<T>(string text) => JsonConvert.DeserializeObject<T>(text);
        internal static string Serialize<T>(T value, WorkshopJsonOptions? options = null) =>
            JsonConvert.SerializeObject(value, options?.WriteIndented == true ? Formatting.Indented : Formatting.None);
    }

    internal sealed class WorkshopLogger
    {
        internal void Msg(string message) => Debug.Log("[Best Offer] " + message);
        internal void Warning(string message) => Debug.LogWarning("[Best Offer] " + message);
    }

    public sealed partial class BestOfferMod
    {
        internal readonly Harmony HarmonyInstance = new("DrNaki.BestOffer.Workshop");
        internal readonly WorkshopLogger LoggerInstance = new();
    }

    public sealed partial class GameTradeAdapter
    {
        // Same native one-shot dialogue tail, now private in the release build.
        static bool PlayNativeDealDialogueOnce(StoreClient client) =>
            (bool)AccessTools.Method(typeof(NegociationUIManager), "PlayAcceptDealDialogueOnce")
                .Invoke(null, new object[] { client });
    }
}
