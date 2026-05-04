using System.IO;
using UnityEngine;
using UnityEditor;
using LLMUnity;
using BergischeDiakonie.Speech;
using UnityEditor.SceneManagement;

/// <summary>
/// Diakonie > Setup Local LLM — richtet die komplette LLM+RAG-Pipeline ein.
/// Sucht GGUF-Dateien automatisch in tools/models_output/llm/ und setzt sie.
/// </summary>
public static class DiakonieLocalLLMSetup
{
    static readonly string ModelsDir = Path.Combine(
        Application.dataPath.Replace("/Assets", ""), "tools", "models_output", "llm");

    const string ChatGguf  = "qwen2.5-1.5b-instruct-q4_k_m.gguf";
    const string EmbedGguf = "nomic-embed-text-v1.5.Q4_K_M.gguf";

    [MenuItem("Diakonie/Setup Local LLM")]
    static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != "Main")
        {
            Debug.LogWarning("[LocalLLMSetup] Bitte Main-Scene öffnen.");
            return;
        }

        // ── 1. LocalLLM GameObject ────────────────────────────────────────
        var go = GameObject.Find("LocalLLM") ?? new GameObject("LocalLLM");

        // ── 2. Chat-LLM ───────────────────────────────────────────────────
        var allLlms = go.GetComponents<LLM>();
        LLM llm = allLlms.Length > 0 ? allLlms[0] : go.AddComponent<LLM>();
        llm.dontDestroyOnLoad = false;
        llm.numThreads = 4;
        llm.numGPULayers = 0;
        llm.contextSize = 4096;
        llm.batchSize = 512;
        SetModelIfFound(llm, ChatGguf, "Chat");

        // ── 3. Embedding-LLM ──────────────────────────────────────────────
        LLM llmEmbed = allLlms.Length > 1 ? allLlms[1] : go.AddComponent<LLM>();
        llmEmbed.dontDestroyOnLoad = false;
        llmEmbed.numThreads = 2;
        llmEmbed.numGPULayers = 0;
        llmEmbed.contextSize = 512;
        llmEmbed.batchSize = 512;
        SetModelIfFound(llmEmbed, EmbedGguf, "Embedding");

        // ── 4. LLMAgent ───────────────────────────────────────────────────
        var agent = go.GetComponent<LLMAgent>() ?? go.AddComponent<LLMAgent>();
        agent.llm = llm;
        agent.systemPrompt =
            "Du bist ein präziser medizinischer Dokumentationsassistent der Bergischen Diakonie. " +
            "Du formulierst Pflegeprotokolle professionell, sachlich und nach Pflegestandards. " +
            "Antworte ausschließlich auf Deutsch. Erfinde keine Informationen.";
        EditorUtility.SetDirty(agent);

        // ── 5. RAG ────────────────────────────────────────────────────────
        var rag = go.GetComponent<RAG>() ?? go.AddComponent<RAG>();
        rag.Init(SearchMethods.DBSearch, ChunkingMethods.SentenceSplitter, llmEmbed);
        EditorUtility.SetDirty(rag);

        // ── 6. LocalDocumentationService ──────────────────────────────────
        var docService = go.GetComponent<LocalDocumentationService>()
            ?? go.AddComponent<LocalDocumentationService>();
        docService.llmAgent = agent;
        docService.rag      = rag;
        docService.guidelinesFileName = "guidelines_pflege.txt";
        docService.ragStorePath       = "PflegeRAG.zip";
        EditorUtility.SetDirty(docService);

        // ── 7. Scene-Verdrahtung ──────────────────────────────────────────
        var careDocUI = Object.FindFirstObjectByType<CareDocUI>();
        if (careDocUI != null) { careDocUI.localDocService = docService; EditorUtility.SetDirty(careDocUI); }
        else Debug.LogWarning("[LocalLLMSetup] CareDocUI nicht gefunden.");

        var mgr = Object.FindFirstObjectByType<CareDocumentationManager>();
        if (mgr != null) { mgr.localDocService = docService; EditorUtility.SetDirty(mgr); }
        else Debug.LogWarning("[LocalLLMSetup] CareDocumentationManager nicht gefunden.");

        // ── 8. Speichern ──────────────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        Debug.Log("[LocalLLMSetup] ✓ Fertig. Szene gespeichert.");
    }

    static void SetModelIfFound(LLM llm, string filename, string label)
    {
        string fullPath = Path.Combine(ModelsDir, filename);
        if (File.Exists(fullPath))
        {
            llm.SetModel(fullPath);
            EditorUtility.SetDirty(llm);
            Debug.Log($"[LocalLLMSetup] {label}-GGUF gesetzt: {filename}");
        }
        else
        {
            EditorUtility.SetDirty(llm);
            Debug.LogWarning($"[LocalLLMSetup] {label}-GGUF nicht gefunden: {fullPath}\n" +
                             $"Bitte: python tools/download_llm_model.py" +
                             (label == "Embedding" ? " --model nomic-embed" : ""));
        }
    }
}
