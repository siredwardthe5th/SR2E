using System.Collections;
using System.Linq;
using System;
using Il2CppInterop.Runtime.Injection;
using SR2E.Enums;
using SR2E.Managers;
using SR2E.Storage;

namespace SR2E.Patches.Context;

[HarmonyPatch(typeof(SystemContext), nameof(SystemContext.Start))]
internal class SystemContextPatch
{
    internal static bool didStart = false;
    internal static Dictionary<string, Shader> loadedShaders = new ();
    internal static Il2CppAssetBundle bundle = null;
    internal static Dictionary<string, Type> menusToInit = new ();
    internal static Dictionary<string, Object> bundleAssetsByName = new (StringComparer.OrdinalIgnoreCase);

    static List<Object> assets = new ();
    const string menuPath = "Assets/Menus/";
    const string popUpPath = "Assets/PopUps/";
    const string prefabSuffix = ".prefab";

    internal static string getPopUpPath(string identifier, SR2EMenuTheme currentTheme)
    {
        string extraTheme = "";
        if (currentTheme != SR2EMenuTheme.Default) extraTheme = "_"+currentTheme.ToString().Split(".")[0];
        return $"{popUpPath}{identifier}{extraTheme}{prefabSuffix}";
    }
    internal static string getMenuPath(MenuIdentifier menuIdentifier)
    {
        SR2ESaveManager.data.themes.TryAdd(menuIdentifier.saveKey, menuIdentifier.defaultTheme);
        SR2EMenuTheme currentTheme = SR2ESaveManager.data.themes[menuIdentifier.saveKey];
        List<SR2EMenuTheme> validThemes = MenuEUtil.GetValidThemes(menuIdentifier.saveKey);
        if (validThemes.Count == 0) return null;
        if(!validThemes.Contains(currentTheme)) currentTheme = validThemes.First();
        SR2ESaveManager.Save();
        string extraTheme = "";
        if (currentTheme != SR2EMenuTheme.Default) extraTheme = "_"+currentTheme.ToString().Split(".")[0];
        return $"{menuPath}{menuIdentifier.saveKey}{extraTheme}{prefabSuffix}";
    }

    internal static void Prefix() { didStart = true; }

    internal static void Postfix(SystemContext __instance)
    {
        if(ChangeSystemContextIsModded.HasFlag()) SystemContext.IsModded = true;
        bundle = EmbeddedResourceEUtil.LoadIl2CppBundle("Assets.srtwoessentials.assetbundle");
        if (bundle == null) { MelonLogger.Error("[SR2E] Asset bundle failed to load"); return; }

        string[] allAssetNames = bundle.GetAllAssetNames();

        foreach (string path in allAssetNames)
        {
            if (!path.StartsWith(menuPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.EndsWith(prefabSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            string menu = path.Substring(menuPath.Length, path.Length - menuPath.Length - prefabSuffix.Length);
            SR2EMenuTheme theme = SR2EMenuTheme.Default;
            var split = menu.Split("_");
            var key = split[0];
            if (menu.Contains("_"))
            {
                if (Enum.TryParse(typeof(SR2EMenuTheme), split[1], true, out object result))
                    theme = (SR2EMenuTheme)result;
                else continue;
            }
            if (!MenuEUtil.validThemes.ContainsKey(key)) MenuEUtil.validThemes.Add(key, new List<SR2EMenuTheme>());
            MenuEUtil.validThemes[key].Add(theme);
        }

        foreach (string name in allAssetNames)
        {
            var asset = bundle.LoadAsset(name);
            if (asset == null) { MelonLogger.Warning($"[SR2E] Failed to load asset: {name}"); continue; }
            if (asset.TryCast<Shader>() != null)
            {
                var shader = asset.Cast<Shader>();
                shader.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                loadedShaders[asset.name] = shader;
            }
            assets.Add(asset);
            bundleAssetsByName[asset.name] = asset;
        }

        if (assets.Count == 0) { MelonLogger.Error("[SR2E] No assets loaded from bundle"); return; }

        MelonCoroutines.Start(SetupMenusCoroutine(__instance));

        var lang = __instance.LocalizationDirector.GetCurrentLocaleCode();
        LoadLanguage(lang);

        foreach (var expansion in SR2EEntryPoint.expansionsV1V2)
            try { expansion.OnSystemContext(__instance); }
            catch (Exception e) { MelonLogger.Error(e); }
        foreach (var expansion in SR2EEntryPoint.expansionsV3)
            try { expansion.AfterSystemContext(__instance); }
            catch (Exception e) { MelonLogger.Error(e); }
        SR2ECallEventManager.ExecuteWithArgs(CallEvent.AfterSystemContextLoad, ("systemContext", __instance));
    }

    private static IEnumerator SetupMenusCoroutine(SystemContext systemContext)
    {
        foreach (var obj in assets)
        {
            if (obj == null) continue;
            if (obj.name != "AllMightyMenus") continue;

            var sr2eStuff = Object.Instantiate(obj).TryCast<GameObject>();
            SR2ELogManager.Start();
            SR2ESaveManager.Start();
            SR2ECommandManager.Start();
            SR2ERepoManager.Start();
            SR2EEntryPoint.SR2EStuff = sr2eStuff;
            sr2eStuff.name = "SR2EStuff";
            sr2eStuff.SetActive(false);
            GameObject.DontDestroyOnLoad(sr2eStuff);

            ExecuteInTicks(() =>
            {
                foreach (var melonBase in MelonBase.RegisteredMelons)
                {
                    var exporters = melonBase.MelonAssembly.Assembly.GetTypes()
                        .Where(t => t.IsSubclassOf(typeof(SR2EMenu)) && !t.IsAbstract);
                    foreach (Type type in exporters)
                        try
                        {
                            var identifier = type.GetMenuIdentifierByType();
                            if (!string.IsNullOrWhiteSpace(identifier.saveKey))
                            {
                                var path = getMenuPath(identifier);
                                bool assetEmpty = true;
                                if (!string.IsNullOrWhiteSpace(path))
                                {
                                    string baseName = System.IO.Path.GetFileNameWithoutExtension(path);
                                    assetEmpty = !bundleAssetsByName.ContainsKey(baseName);
                                }

                                UnityEngine.Object rootObject = type.GetMenuRootObject();

                                if (!assetEmpty && rootObject == null)
                                {
                                    string baseName = System.IO.Path.GetFileNameWithoutExtension(path);
                                    if (bundleAssetsByName.TryGetValue(baseName, out var menuAsset))
                                        rootObject = GameObject.Instantiate(menuAsset, sr2eStuff.transform);
                                }

                                if (rootObject == null)
                                {
                                    var message = $"The menu under the name {type.Name} couldn't be loaded! It's root object is null!";
                                    MelonLogger.Error(message);
                                    throw new Exception(message);
                                }

                                rootObject.Cast<GameObject>().transform.SetParent(sr2eStuff.transform);
                                menusToInit.Add(rootObject.name, type);
                                if (!ClassInjector.IsTypeRegisteredInIl2Cpp(type))
                                    ClassInjector.RegisterTypeInIl2Cpp(type,
                                        new RegisterTypeOptions() { LogSuccess = false });
                            }
                            else MelonLogger.Error($"The menu under the name {type.Name} couldn't be loaded! It's MenuIdentifier is broken!");
                        }
                        catch (Exception e) { MelonLogger.Error(e); }
                }

                ExecuteInTicks(() =>
                {
                    sr2eStuff.SetActive(true);
                    foreach (var pair in new Dictionary<string, Type>(menusToInit))
                    foreach (var child in sr2eStuff.GetChildren())
                        if (child.name == pair.Key)
                        {
                            try
                            {
                                child.AddComponent(pair.Value);
                                child.gameObject.SetActive(true);
                                menusToInit.Remove(pair.Key);
                            }
                            catch (Exception e) { MelonLogger.Error(e); }
                        }
                    SR2EEntryPoint.menusFinished = true;
                }, 1);
            }, 1);
            break;
        }
        yield break;
    }
}
