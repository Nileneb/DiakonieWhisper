using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using BergischeDiakonie.Speech;

/// <summary>
/// Diakonie > Setup Character — baut HatWanderer.glb als animierten Charakter
/// per Viewport-Rect in der Ecke ein (kein RenderTexture, 2D-Pipeline bleibt unberührt).
/// </summary>
public static class DiakonieCharacterSetup
{
    const string GlbPath        = "Assets/Mesh/HatWanderer.glb";
    const string ControllerPath = "Assets/Animations/HatWandererController.controller";
    const int    CharLayer      = 8;
    const string CharLayerName  = "Character";

    [MenuItem("Diakonie/Setup Character")]
    static void Run()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main")
        {
            Debug.LogWarning("[CharacterSetup] Bitte Main-Scene öffnen.");
            return;
        }

        EnsureLayer(CharLayerName, CharLayer);

        // Main Camera sieht den Character-Layer nicht
        if (Camera.main != null)
        {
            Camera.main.cullingMask &= ~(1 << CharLayer);
            EditorUtility.SetDirty(Camera.main);
        }

        // Altes RT-Panel entfernen (blockierte BtnUpload-Raycasts)
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            var oldPanel = canvas.transform.Find("CharacterPanel");
            if (oldPanel != null) Object.DestroyImmediate(oldPanel.gameObject);
        }

        // Stage-Container weit weg von der 2D-Szene
        var stage = GameObject.Find("CharacterStage") ?? new GameObject("CharacterStage");
        stage.transform.position = new Vector3(1000, 0, 0);

        SetupCharacterCamera(stage);
        var charGO = SetupCharacterMesh(stage);

        if (charGO != null)
        {
            if (charGO.GetComponent<CharacterAnimator>() == null)
                charGO.AddComponent<CharacterAnimator>();
            EditorUtility.SetDirty(charGO);
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        Debug.Log("[CharacterSetup] ✓ HatWanderer eingebaut (Viewport). Play drücken um Animationen zu sehen.");
    }

    // ── CharacterCamera (Viewport-Rect, kein RenderTexture) ───────────────

    static void SetupCharacterCamera(GameObject stage)
    {
        var existing = stage.transform.Find("CharacterCamera");
        var camGO = existing != null ? existing.gameObject : new GameObject("CharacterCamera");
        camGO.transform.SetParent(stage.transform, false);
        camGO.transform.localPosition = new Vector3(0, 110f, -280f);
        camGO.transform.localRotation = Quaternion.Euler(22f, 0f, 0f); // leicht nach unten auf Char

        var cam = camGO.GetComponent<Camera>();
        if (cam == null) cam = camGO.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.Depth; // rendert über Main Camera ohne Background zu löschen
        cam.backgroundColor = Color.clear;
        cam.cullingMask     = 1 << CharLayer;
        cam.targetTexture   = null;
        cam.depth           = 1;   // nach Main Camera (depth 0 oder -1)
        cam.fieldOfView     = 38f;
        cam.nearClipPlane   = 1f;
        cam.farClipPlane    = 2000f;
        cam.allowHDR        = false;
        cam.allowMSAA       = false;
        cam.rect            = new Rect(0.72f, 0.02f, 0.26f, 0.42f); // unten-rechts (normalized screen)
        EditorUtility.SetDirty(cam);

        // CharacterView (altes RT-Script) entfernen falls noch vorhanden
        var oldView = camGO.GetComponent<CharacterView>();
        if (oldView != null) Object.DestroyImmediate(oldView);
    }

    // ── Character Mesh + AnimatorController ───────────────────────────────

    static GameObject SetupCharacterMesh(GameObject stage)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
        if (prefab == null)
        {
            Debug.LogError($"[CharacterSetup] GLB nicht gefunden: {GlbPath}");
            return null;
        }

        var old = stage.transform.Find("HatWanderer");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var charGO = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        charGO.name = "HatWanderer";
        charGO.transform.SetParent(stage.transform, false);
        charGO.transform.localPosition = Vector3.zero;
        charGO.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        charGO.transform.localScale    = Vector3.one * 100f; // Meshy.ai GLB = 0.01 Unity-Units
        SetLayerRecursive(charGO, CharLayer);

        var controller = BuildAnimatorController();
        var anim = charGO.GetComponentInChildren<Animator>();
        if (anim == null) anim = charGO.AddComponent<Animator>();
        if (controller != null) anim.runtimeAnimatorController = controller;

        return charGO;
    }

    static AnimatorController BuildAnimatorController()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(GlbPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToArray();

        if (clips.Length == 0)
        {
            Debug.LogWarning("[CharacterSetup] Keine AnimationClips im GLB gefunden.");
            return null;
        }

        Directory.CreateDirectory("Assets/Animations");
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null) AssetDatabase.DeleteAsset(ControllerPath);

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm   = ctrl.layers[0].stateMachine;

        foreach (var clip in clips)
        {
            var state  = sm.AddState(clip.name);
            state.motion = clip;
        }

        Debug.Log($"[CharacterSetup] AnimatorController mit {clips.Length} Clips erstellt.");
        return ctrl;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursive(t.gameObject, layer);
    }

    static void EnsureLayer(string layerName, int index)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset"));
        var layers = tagManager.FindProperty("layers");
        var prop   = layers.GetArrayElementAtIndex(index);
        if (!string.IsNullOrEmpty(prop.stringValue)) return;
        prop.stringValue = layerName;
        tagManager.ApplyModifiedProperties();
        Debug.Log($"[CharacterSetup] Layer {index} als '{layerName}' angelegt.");
    }
}
