using System.Collections;
using System.Linq;
using System;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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

        foreach (string path in bundle.GetAllAssetNames())
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

        MelonCoroutines.Start(LoadBundleAssetsCoroutine(__instance));

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

    // LoadAsset_Internal is NOT a registered icall in this build.
    // LoadAssetAsync_Internal IS registered. Invoke it per-asset via il2cpp_runtime_invoke,
    // passing an IL2CPP string pointer (ManagedStringToIl2Cpp) to bypass ReadOnlySpan marshaling.
    static IntPtr _loadAssetAsyncMethodPtr = IntPtr.Zero;
    static IntPtr _objectTypeObjPtr = IntPtr.Zero;

    private static unsafe IntPtr InvokeLoadAssetAsync(IntPtr bundleNativePtr, string name)
    {
        if (_loadAssetAsyncMethodPtr == IntPtr.Zero)
        {
            foreach (var f in typeof(AssetBundle).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (f.FieldType != typeof(IntPtr)) continue;
                if (!f.Name.Contains("LoadAssetAsync_Internal_Private")) continue;
                if (f.Name.Contains("Injected")) continue;
                var v = (IntPtr)f.GetValue(null);
                if (v != IntPtr.Zero) { _loadAssetAsyncMethodPtr = v; break; }
            }
        }
        if (_loadAssetAsyncMethodPtr == IntPtr.Zero) return IntPtr.Zero;

        if (_objectTypeObjPtr == IntPtr.Zero)
            _objectTypeObjPtr = IL2CPP.il2cpp_type_get_object(
                IL2CPP.il2cpp_class_get_type(Il2CppClassPointerStore<Object>.NativeClassPtr));
        if (_objectTypeObjPtr == IntPtr.Zero) return IntPtr.Zero;

        IntPtr strPtr = IL2CPP.ManagedStringToIl2Cpp(name);
        if (strPtr == IntPtr.Zero) return IntPtr.Zero;

        IntPtr exc = IntPtr.Zero;
        void** args = stackalloc void*[2];
        args[0] = (void*)strPtr;
        args[1] = (void*)_objectTypeObjPtr;
        IntPtr result = IL2CPP.il2cpp_runtime_invoke(_loadAssetAsyncMethodPtr, bundleNativePtr, args, ref exc);
        return exc != IntPtr.Zero ? IntPtr.Zero : result;
    }

    private static IEnumerator LoadBundleAssetsCoroutine(SystemContext systemContext)
    {
        IntPtr bundleNativePtr = IntPtr.Zero;
        foreach (var f in typeof(Il2CppAssetBundle).GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
        {
            if (f.FieldType != typeof(IntPtr)) continue;
            var val = (IntPtr)f.GetValue(bundle);
            if (val != IntPtr.Zero) { bundleNativePtr = val; break; }
        }
        if (bundleNativePtr == IntPtr.Zero) { MelonLogger.Error("[SR2E] Could not get native bundle pointer"); yield break; }

        string[] assetNames = bundle.GetAllAssetNames();
        if (assetNames == null || assetNames.Length == 0) { MelonLogger.Error("[SR2E] bundle.GetAllAssetNames() returned empty"); yield break; }

        foreach (string name in assetNames)
        {
            IntPtr reqPtr = IntPtr.Zero;
            Exception loadErr = null;
            try { reqPtr = InvokeLoadAssetAsync(bundleNativePtr, name); }
            catch (Exception e) { loadErr = e; }

            if (loadErr != null || reqPtr == IntPtr.Zero) { MelonLogger.Warning($"[SR2E] LoadAssetAsync failed for {name}: {loadErr?.Message}"); continue; }

            var req = new AssetBundleRequest(reqPtr);
            while (!req.isDone) yield return null;

            var asset = req.asset;
            if (asset == null) continue;
            if (asset.TryCast<Shader>() != null)
            {
                var shader = asset.Cast<Shader>();
                shader.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                loadedShaders[asset.name] = shader;
            }
            assets.Add(asset);
            bundleAssetsByName[asset.name] = asset;
        }

        if (assets.Count == 0) { MelonLogger.Error("[SR2E] No assets loaded from bundle"); yield break; }

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
    }
}
