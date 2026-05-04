#if UNITY_EDITOR
using UnityEditor;
using UndreamAI.LlamaLib;

// Before domain reload, unregister the managed logging callback from LlamaLib's native library.
// Without this, the .so stays loaded after domain reload but holds a stale GC handle to the
// old domain's delegate, producing "Resolve of invalid GC handle" warnings on every reload.
[InitializeOnLoad]
static class LLMUnityDomainReloadFix
{
    static LLMUnityDomainReloadFix()
    {
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
    }

    static void OnBeforeAssemblyReload()
    {
        LlamaLib.LoggingStop();
    }
}
#endif
