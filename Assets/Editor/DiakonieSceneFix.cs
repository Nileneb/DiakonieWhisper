using UnityEngine;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;
using LLMUnity;
using BergischeDiakonie.Speech;

/// <summary>
/// Einmaliger Fix: verschiebt CharacterPanel in den sichtbaren Canvas ("Canvas", SortingOrder 1)
/// und verdrahtet CareDocUI.panelFilePath / inputFilePath auf die richtigen Objekte.
/// Root cause: DiakonieCharacterSetup + DiakonieUISetup fanden per FindFirstObjectByType
/// zuerst Canvas "ModelLoader" (SortingOrder 0), der hinter Canvas "Canvas" (SortingOrder 1) liegt.
/// </summary>
public static class DiakonieSceneFix
{
    [MenuItem("Diakonie/Fix Scene")]
    static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != "Main")
        {
            Debug.LogWarning("[SceneFix] Bitte Main-Scene öffnen.");
            return;
        }

        var careDocUI = Object.FindFirstObjectByType<CareDocUI>();
        if (careDocUI == null) { Debug.LogError("[SceneFix] CareDocUI nicht gefunden."); return; }

        var canvas2GO = GameObject.Find("Canvas");
        if (canvas2GO == null) { Debug.LogError("[SceneFix] Canvas 'Canvas' nicht gefunden."); return; }

        // ── 1. CharacterPanel suchen und in Canvas "Canvas" korrekt positionieren ─
        // Reihenfolge: erst ModelLoader, dann direkt in Canvas (falls schon migriert)
        Transform charPanel = null;
        var canvas1GO = GameObject.Find("ModelLoader");
        if (canvas1GO != null)
            charPanel = canvas1GO.transform.Find("CharacterPanel");
        if (charPanel == null)
            charPanel = canvas2GO.transform.Find("CharacterPanel");

        if (charPanel != null)
        {
            if (charPanel.parent != canvas2GO.transform)
            {
                charPanel.SetParent(canvas2GO.transform, false);
                Debug.Log("[SceneFix] CharacterPanel → Canvas 'Canvas' verschoben.");
            }
            charPanel.SetAsLastSibling();

            var rt = charPanel.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0, 0);
            rt.anchorMax        = new Vector2(0, 0);
            rt.pivot            = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(12, 12);
            rt.sizeDelta        = new Vector2(200, 640);

            EditorUtility.SetDirty(charPanel.gameObject);
            Debug.Log("[SceneFix] CharacterPanel RectTransform → bottom-left (200x320) gesetzt.");
        }
        else
        {
            Debug.LogWarning("[SceneFix] CharacterPanel nicht gefunden — 'Diakonie > Setup Character' ausführen.");
        }

        // ── 2. panelFilePath auf Canvas "Canvas" verdrahten ──────────────────
        var panelFilePath2 = canvas2GO.transform.Find("PanelFilePath");
        if (panelFilePath2 == null)
        {
            Debug.LogError("[SceneFix] PanelFilePath in Canvas 'Canvas' nicht gefunden.");
            return;
        }
        careDocUI.panelFilePath = panelFilePath2.gameObject;
        Debug.Log("[SceneFix] CareDocUI.panelFilePath → Canvas-2-PanelFilePath verdrahtet.");

        // ── 3. inputFilePath auf Canvas "Canvas" verdrahten ──────────────────
        var inputFilePathTf = panelFilePath2.Find("InputFilePath");
        if (inputFilePathTf != null)
        {
            var inputField = inputFilePathTf.GetComponent<TMP_InputField>();
            if (inputField != null)
            {
                careDocUI.inputFilePath = inputField;
                Debug.Log("[SceneFix] CareDocUI.inputFilePath → Canvas-2-InputFilePath verdrahtet.");
            }
            else { Debug.LogWarning("[SceneFix] TMP_InputField auf InputFilePath nicht gefunden."); }
        }
        else { Debug.LogWarning("[SceneFix] InputFilePath unter PanelFilePath nicht gefunden."); }

        // ── 4. BtnConfirmLoad → CareDocUI.LoadFromInputPath (persistent) ─────
        var confirmBtnTf = panelFilePath2.Find("BtnConfirmLoad");
        if (confirmBtnTf != null)
        {
            var confirmBtn = confirmBtnTf.GetComponent<Button>();
            if (confirmBtn != null)
            {
                // Bestehende Persistent Listeners löschen (Duplikat-Schutz)
                var so = new SerializedObject(confirmBtn);
                var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                calls.ClearArray();
                so.ApplyModifiedProperties();

                UnityEventTools.AddPersistentListener(confirmBtn.onClick, careDocUI.LoadFromInputPath);
                EditorUtility.SetDirty(confirmBtn);
                Debug.Log("[SceneFix] BtnConfirmLoad.onClick → CareDocUI.LoadFromInputPath verdrahtet.");
            }
        }
        else { Debug.LogWarning("[SceneFix] BtnConfirmLoad unter Canvas-2-PanelFilePath nicht gefunden."); }

        // ── 5. CharacterCamera: CharacterView auto-framing ───────────────────
        var stageGO = GameObject.Find("CharacterStage");
        if (stageGO != null)
        {
            var camTf  = stageGO.transform.Find("CharacterCamera");
            var charTf = stageGO.transform.Find("HatWanderer");

            if (camTf != null)
            {
                // Reset to safe initial position — CharacterView corrects at Play-time
                camTf.localPosition = new Vector3(0, 90f, -350f);
                camTf.localRotation = Quaternion.identity;
                var cam = camTf.GetComponent<Camera>();
                if (cam != null) { cam.fieldOfView = 50f; EditorUtility.SetDirty(cam); }

                // Add/update CharacterView so it auto-frames from actual mesh bounds
                var cv = camTf.GetComponent<CharacterView>() ?? camTf.gameObject.AddComponent<CharacterView>();
                if (charTf != null) cv.target = charTf;
                else Debug.LogWarning("[SceneFix] HatWanderer nicht gefunden — CharacterView.target bleibt leer.");

                EditorUtility.SetDirty(camTf.gameObject);
                Debug.Log("[SceneFix] CharacterCamera → CharacterView mit target HatWanderer gesetzt.");
            }
            else { Debug.LogWarning("[SceneFix] CharacterCamera nicht gefunden."); }
        }
        else { Debug.LogWarning("[SceneFix] CharacterStage nicht gefunden — 'Diakonie > Setup Character' ausführen."); }

        // ── 6. BtnLLM verdrahten ─────────────────────────────────────────────
        var basicPanel = GameObject.Find("BasicButtonsPanel");
        if (basicPanel != null)
        {
            var llmBtnTf = basicPanel.transform.Find("BtnLLM");
            if (llmBtnTf != null)
            {
                var llmBtn = llmBtnTf.GetComponent<Button>();
                if (llmBtn != null)
                {
                    careDocUI.btnLLM = llmBtn;
                    EditorUtility.SetDirty(llmBtn.gameObject);
                    Debug.Log("[SceneFix] CareDocUI.btnLLM verdrahtet.");
                }
            }
            else { Debug.LogWarning("[SceneFix] BtnLLM nicht gefunden — erst 'Diakonie > Setup UI' ausführen."); }
        }

        // ── 7. LLM contextSize auf 8192 setzen ───────────────────────────────
        var localLLM = GameObject.Find("LocalLLM");
        if (localLLM != null)
        {
            var llm = localLLM.GetComponent<LLM>();
            if (llm != null)
            {
                llm.contextSize = 8192;
                EditorUtility.SetDirty(llm);
                Debug.Log("[SceneFix] LLM.contextSize = 8192");
            }
            else { Debug.LogWarning("[SceneFix] LLM-Komponente auf LocalLLM nicht gefunden."); }
        }
        else { Debug.LogWarning("[SceneFix] LocalLLM GameObject nicht gefunden."); }

        // ── 8. Szene speichern ────────────────────────────────────────────────
        EditorUtility.SetDirty(careDocUI);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        Debug.Log("[SceneFix] ✓ Fertig. Play drücken zum Testen.");
    }
}
