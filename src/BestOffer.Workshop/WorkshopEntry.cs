using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeepSpaceBestOffer;

public sealed class WorkshopEntry : IMod
{
    BestOfferMod? mod;
    WorkshopModMenuUI? menuLayout;
    public void Init(ModManifest manifest) { }
    public void OnEnable()
    {
        if (mod != null) return;
        System.IO.Directory.CreateDirectory(WorkshopRuntime.UserDataDirectory);
        var host = new GameObject("BestOffer.Workshop");
        UnityEngine.Object.DontDestroyOnLoad(host);
        try
        {
            mod = host.AddComponent<BestOfferMod>();
            mod.Initialize();
            menuLayout = host.AddComponent<WorkshopModMenuUI>();
            SceneManager.sceneLoaded += SceneLoaded;
        }
        catch
        {
            mod?.DisposeWorkshopSettings();
            if (mod != null) mod.HarmonyInstance.UnpatchAll("DrNaki.BestOffer.Workshop");
            UnityEngine.Object.Destroy(host);
            mod = null;
            throw;
        }
    }
    void SceneLoaded(Scene scene, LoadSceneMode mode) => mod?.SceneLoaded(scene.buildIndex, scene.name);
    public void OnDisable()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        if (mod == null) return;
        mod.enabled = false;
        mod.DisposeWorkshopSettings();
        if (menuLayout != null) { menuLayout.Restore(); menuLayout.enabled = false; }
        mod.HarmonyInstance.UnpatchAll("DrNaki.BestOffer.Workshop");
        UnityEngine.Object.Destroy(mod.gameObject);
        mod = null;
    }
}
