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
        var llm = existing.GetComponent<LLM>() ?? existing.AddComponent<LLM>();
        llm.dontDestroyOnLoad = false;
        llm.numThreads = 4;
        llm.numGPULayers = 0;   // 0 = CPU-only; auf GPU-Rechnern erhöhen (z.B. 99)
        llm.contextSize = 4096;
        llm.batchSize = 512;
        EditorUtility.SetDirty(llm);
        Debug.Log("[LocalLLMSetup] LLM-Komponente konfiguriert. Bitte GGUF-Modell im Inspector setzen.");

        // ── 3. LLMAgent (Chat-Interface) ─────────────────────────────────
        var agent = existing.GetComponent<LLMAgent>() ?? existing.AddComponent<LLMAgent>();
        agent.llm = llm;
        agent.systemPrompt =
            "Du bist ein präziser medizinischer Dokumentationsassistent der Bergischen Diakonie. " +
            "Du formulierst Pflegeprotokolle professionell, sachlich und nach Pflegestandards. " +
            "Antworte ausschließlich auf Deutsch. Erfinde keine Informationen.";
        EditorUtility.SetDirty(agent);

        // ── 4. RAG (Wissens-Retrieval) ────────────────────────────────────
        var rag = existing.GetComponent<RAG>() ?? existing.AddComponent<RAG>();
        rag.Init(SearchMethods.SimpleSearch, ChunkingMethods.NoChunking, llm);
        EditorUtility.SetDirty(rag);
        Debug.Log("[LocalLLMSetup] RAG-Komponente konfiguriert.");

        // ── 5. LocalDocumentationService ────────────────────────────────
        var docService = existing.GetComponent<LocalDocumentationService>()
            ?? existing.AddComponent<LocalDocumentationService>();
        docService.llmAgent = agent;
        docService.rag      = rag;
        docService.guidelinesFileName = "guidelines_pflege.txt";
        docService.ragStorePath       = "PflegeRAG.zip";
        EditorUtility.SetDirty(docService);
        Debug.Log("[LocalLLMSetup] LocalDocumentationService verdrahtet.");

        // ── 6. CareDocUI + CareDocumentationManager verdrahten ──────────
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

        // ── 7. Szene speichern ────────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        Debug.Log("[LocalLLMSetup] ✓ Setup abgeschlossen.\n" +
                  "Nächster Schritt: Im Inspector von 'LocalLLM' → LLM-Komponente die GGUF-Datei setzen.\n" +
                  "Empfehlung: tools/download_llm_model.py ausführen für Qwen2.5-1.5B-Q4.");
    }
}
