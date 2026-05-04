using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using BergischeDiakonie.Speech;

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

        if (Camera.main != null)
        {
            Camera.main.cullingMask &= ~(1 << CharLayer);
            EditorUtility.SetDirty(Camera.main);
        }

        var stage = GameObject.Find("CharacterStage") ?? new GameObject("CharacterStage");
        stage.transform.position = new Vector3(1000, 0, 0);

        SetupUI(rt);
        SetupCharacterCamera(stage, rt);
        var charGO = SetupCharacterMesh(stage);

        if (charGO != null)
        {
            // CharacterAnimator drives clip cycling
            if (charGO.GetComponent<CharacterAnimator>() == null)
                charGO.AddComponent<CharacterAnimator>();

            // CharacterView on camera auto-frames at runtime
            var camTf = stage.transform.Find("CharacterCamera");
            if (camTf != null)
            {
                var cv = camTf.GetComponent<CharacterView>() ?? camTf.gameObject.AddComponent<CharacterView>();
                cv.target = charGO.transform;
                EditorUtility.SetDirty(camTf.gameObject);
            }

            EditorUtility.SetDirty(charGO);
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        Debug.Log("[CharacterSetup] ✓ Fertig. Play drücken — CharacterView framt den Charakter automatisch.");
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
        // Initial estimate — CharacterView corrects this at runtime via bounds
        camGO.transform.localPosition = new Vector3(0, 90f, -350f);
        camGO.transform.localRotation = Quaternion.identity;

        var cam = camGO.GetComponent<Camera>() ?? camGO.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;
        cam.cullingMask     = 1 << CharLayer;
        cam.targetTexture   = rt;
        cam.depth           = 0;
        cam.rect            = new Rect(0, 0, 1, 1);
        cam.fieldOfView     = 50f;
        cam.nearClipPlane   = 1f;
        cam.farClipPlane    = 2000f;
        cam.allowHDR        = false;
        cam.allowMSAA       = false;
        EditorUtility.SetDirty(cam);
    }

    // ── UI RawImage ───────────────────────────────────────────────────────

    static void SetupUI(RenderTexture rt)
    {
        // Always target the named canvas "Canvas" (SortingOrder 1), not the first found
        var canvasGO = GameObject.Find("Canvas");
        var canvas = canvasGO?.GetComponent<Canvas>();
        if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) { Debug.LogError("[CharacterSetup] Canvas nicht gefunden."); return; }

        var old = canvas.transform.Find("CharacterPanel");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var panelGO = new GameObject("CharacterPanel");
        panelGO.transform.SetParent(canvas.transform, false);

        var img = panelGO.AddComponent<RawImage>();
        img.texture       = rt;
        img.color         = Color.white;
        img.raycastTarget = false;

        var rect = panelGO.GetComponent<RectTransform>();
        rect.anchorMin        = Vector2.zero;
        rect.anchorMax        = Vector2.zero;
        rect.pivot            = Vector2.zero;
        rect.anchoredPosition = new Vector2(12, 12);
        rect.sizeDelta        = new Vector2(200, 640);

        // Render on top of other UI elements
        panelGO.transform.SetAsLastSibling();

        EditorUtility.SetDirty(panelGO);
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
        var anim = charGO.GetComponentInChildren<Animator>() ?? charGO.AddComponent<Animator>();
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
            Debug.LogWarning("[CharacterSetup] Keine AnimationClips im GLB.");
            return null;
        }

        Directory.CreateDirectory("Assets/Animations");
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null) AssetDatabase.DeleteAsset(ControllerPath);

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm   = ctrl.layers[0].stateMachine;
        foreach (var clip in clips)
            sm.AddState(clip.name).motion = clip;

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
