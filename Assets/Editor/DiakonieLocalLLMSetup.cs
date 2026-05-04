using UnityEngine;
using UnityEditor;
using LLMUnity;
using BergischeDiakonie.Speech;
using UnityEditor.SceneManagement;

/// <summary>
/// Diakonie > Setup Local LLM — richtet die komplette LLM+RAG-Pipeline ein.
/// Danach im Inspector des "LocalLLM" GameObjects die GGUF-Modelldatei setzen.
/// </summary>
public static class DiakonieLocalLLMSetup
{
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
        var existing = GameObject.Find("LocalLLM");
        if (existing != null)
        {
            Debug.Log("[LocalLLMSetup] LocalLLM-Objekt bereits vorhanden, überspringe Erstellung.");
        }
        else
        {
            existing = new GameObject("LocalLLM");
            Debug.Log("[LocalLLMSetup] LocalLLM-Objekt erstellt.");
        }

        // ── 2. LLM (Chat-Modell) ──────────────────────────────────────────
        // Mehrere LLM-Komponenten auf demselben GO werden anhand des Typs unterschieden.
        // Wir nutzen GetComponents<LLM>() um Chat- vs. Embedding-LLM zu trennen.
        var allLlms = existing.GetComponents<LLM>();
        LLM llm = allLlms.Length > 0 ? allLlms[0] : existing.AddComponent<LLM>();
        llm.dontDestroyOnLoad = false;
        llm.numThreads = 4;
        llm.numGPULayers = 0;
        llm.contextSize = 4096;
        llm.batchSize = 512;
        EditorUtility.SetDirty(llm);
        Debug.Log("[LocalLLMSetup] Chat-LLM konfiguriert. Bitte GGUF-Chat-Modell im Inspector setzen.");

        // ── 3. LLM (Embedding-Modell) ─────────────────────────────────────
        // Separates LLM für RAG-Embeddings — vermeidet LLMEmbedder-Warning.
        // Empfohlen: nomic-embed-text-v1.5.Q4_K_M.gguf (~83 MB)
        // Download: python tools/download_llm_model.py --model nomic-embed
        LLM llmEmbed = allLlms.Length > 1 ? allLlms[1] : existing.AddComponent<LLM>();
        llmEmbed.dontDestroyOnLoad = false;
        llmEmbed.numThreads = 2;
        llmEmbed.numGPULayers = 0;
        llmEmbed.contextSize = 512;
        llmEmbed.batchSize = 512;
        EditorUtility.SetDirty(llmEmbed);
        Debug.Log("[LocalLLMSetup] Embedding-LLM konfiguriert. Bitte nomic-embed GGUF im Inspector setzen.");

        // ── 4. LLMAgent (Chat-Interface) ─────────────────────────────────
        var agent = existing.GetComponent<LLMAgent>() ?? existing.AddComponent<LLMAgent>();
        agent.llm = llm;
        agent.systemPrompt =
            "Du bist ein präziser medizinischer Dokumentationsassistent der Bergischen Diakonie. " +
            "Du formulierst Pflegeprotokolle professionell, sachlich und nach Pflegestandards. " +
            "Antworte ausschließlich auf Deutsch. Erfinde keine Informationen.";
        EditorUtility.SetDirty(agent);

        // ── 5. RAG (Wissens-Retrieval) ────────────────────────────────────
        var rag = existing.GetComponent<RAG>() ?? existing.AddComponent<RAG>();
        rag.Init(SearchMethods.DBSearch, ChunkingMethods.SentenceSplitter, llmEmbed);
        EditorUtility.SetDirty(rag);
        Debug.Log("[LocalLLMSetup] RAG mit dediziertem Embedding-LLM konfiguriert.");

        // ── 6. LocalDocumentationService ────────────────────────────────
        var docService = existing.GetComponent<LocalDocumentationService>()
            ?? existing.AddComponent<LocalDocumentationService>();
        docService.llmAgent = agent;
        docService.rag      = rag;
        docService.guidelinesFileName = "guidelines_pflege.txt";
        docService.ragStorePath       = "PflegeRAG.zip";
        EditorUtility.SetDirty(docService);
        Debug.Log("[LocalLLMSetup] LocalDocumentationService verdrahtet.");

        // ── 7. CareDocUI + CareDocumentationManager verdrahten ──────────
        var careDocUI = Object.FindFirstObjectByType<CareDocUI>();
        if (careDocUI != null)
        {
            careDocUI.localDocService = docService;
            EditorUtility.SetDirty(careDocUI);
            Debug.Log("[LocalLLMSetup] CareDocUI.localDocService gesetzt.");
        }
        else Debug.LogWarning("[LocalLLMSetup] CareDocUI nicht gefunden.");

        var manager = Object.FindFirstObjectByType<CareDocumentationManager>();
        if (manager != null)
        {
            manager.localDocService = docService;
            EditorUtility.SetDirty(manager);
            Debug.Log("[LocalLLMSetup] CareDocumentationManager.localDocService gesetzt.");
        }
        else Debug.LogWarning("[LocalLLMSetup] CareDocumentationManager nicht gefunden.");

        // ── 8. Szene speichern ────────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        Debug.Log("[LocalLLMSetup] ✓ Setup abgeschlossen.\n" +
                  "Nächste Schritte:\n" +
                  "  1. python tools/download_llm_model.py              (Qwen2.5-1.5B Chat, ~1GB)\n" +
                  "  2. python tools/download_llm_model.py --model nomic-embed  (Embedding, ~83MB)\n" +
                  "  3. LocalLLM → LLM[0] (Chat): GGUF-Pfad setzen\n" +
                  "  4. LocalLLM → LLM[1] (Embed): nomic-embed GGUF-Pfad setzen");
    }
}
