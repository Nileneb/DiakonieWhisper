using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using BergischeDiakonie.Speech;

/// <summary>
/// Diakonie > Setup Character — baut HatWanderer.glb als animierten Charakter
/// in der Canvas-Ecke ein (RenderTexture → RawImage, über 2D-UI sichtbar).
/// </summary>
public static class DiakonieCharacterSetup
{
    const string GlbPath        = "Assets/Mesh/HatWanderer.glb";
    const string RtPath         = "Assets/RenderTextures/CharacterRT.renderTexture";
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

        var rt = EnsureRenderTexture();

        // Main Camera sieht den Character-Layer nicht
        if (Camera.main != null)
        {
            Camera.main.cullingMask &= ~(1 << CharLayer);
            EditorUtility.SetDirty(Camera.main);
        }

        // Stage-Container weit weg von der 2D-Szene
        var stage = GameObject.Find("CharacterStage") ?? new GameObject("CharacterStage");
        stage.transform.position = new Vector3(1000, 0, 0);

        var rawImg = SetupUI(rt);
        SetupCharacterCamera(stage, rt);
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
        Debug.Log("[CharacterSetup] ✓ HatWanderer eingebaut. Play drücken um Animationen zu sehen.");
    }

    // ── RenderTexture ─────────────────────────────────────────────────────

    static RenderTexture EnsureRenderTexture()
    {
        Directory.CreateDirectory("Assets/RenderTextures");
        AssetDatabase.Refresh();

        var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(RtPath);
        if (existing != null) return existing;

        var rt = new RenderTexture(256, 512, 16, RenderTextureFormat.ARGB32);
        rt.name = "CharacterRT";
        rt.Create();
        AssetDatabase.CreateAsset(rt, RtPath);
        AssetDatabase.SaveAssets();
        return rt;
    }

    // ── CharacterCamera ───────────────────────────────────────────────────

    static void SetupCharacterCamera(GameObject stage, RenderTexture rt)
    {
        var existing = stage.transform.Find("CharacterCamera");
        var camGO = existing != null ? existing.gameObject : new GameObject("CharacterCamera");
        camGO.transform.SetParent(stage.transform, false);
        camGO.transform.localPosition = new Vector3(0, 110f, -280f);
        // Fix 1: 22° nach unten — atan(110/280) ≈ 21.4°, Charakter war außerhalb FOV
        camGO.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);

        var cam = camGO.GetComponent<Camera>();
        if (cam == null) cam = camGO.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;       // transparenter Hintergrund
        cam.cullingMask     = 1 << CharLayer;
        cam.targetTexture   = rt;                // Fix 2: direkt setzen, kein null
        cam.depth           = 0;                 // Main Camera depth=-1
        cam.rect            = new Rect(0, 0, 1, 1);
        cam.fieldOfView     = 38f;
        cam.nearClipPlane   = 1f;
        cam.farClipPlane    = 2000f;
        cam.allowHDR        = false;
        cam.allowMSAA       = false;
        EditorUtility.SetDirty(cam);
    }

    // ── UI RawImage ───────────────────────────────────────────────────────

    static RawImage SetupUI(RenderTexture rt)
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) { Debug.LogError("[CharacterSetup] Canvas nicht gefunden."); return null; }

        var old = canvas.transform.Find("CharacterPanel");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var panelGO = new GameObject("CharacterPanel");
        panelGO.transform.SetParent(canvas.transform, false);

        var img = panelGO.AddComponent<RawImage>();
        img.texture       = rt;
        img.color         = Color.white;
        img.raycastTarget = false; // Fix 3: BtnUpload-Raycasts durchlassen

        var rect = panelGO.GetComponent<RectTransform>();
        rect.anchorMin        = new Vector2(1, 0);
        rect.anchorMax        = new Vector2(1, 0);
        rect.pivot            = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(-12, 12);
        rect.sizeDelta        = new Vector2(200, 320);

        EditorUtility.SetDirty(panelGO);
        return img;
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
        charGO.transform.localScale    = Vector3.one * 100f;
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
