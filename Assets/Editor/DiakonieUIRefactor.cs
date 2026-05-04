using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using UnityEditor.SceneManagement;
using BergischeDiakonie.Speech;

/// <summary>
/// Diakonie > Refactor UI — führt UI-Redesign + Scene-Fixes durch.
/// Einmalig ausführen nach Änderungen.
/// </summary>
public static class DiakonieUIRefactor
{
    // ── Diakonie Dark Theme ───────────────────────────────────────────────
    static readonly Color BG          = Hex("1C2B3A");
    static readonly Color PanelBg     = Hex("243447");
    static readonly Color InputBg     = Hex("111C28");
    static readonly Color Accent      = Hex("0057A8");
    static readonly Color RecordRed   = Hex("C62828");
    static readonly Color StopGrey    = Hex("37474F");
    static readonly Color ExportGreen = Hex("1B5E20");
    static readonly Color UploadBlue  = Hex("1565C0");
    static readonly Color TextMain    = Hex("ECEFF1");
    static readonly Color TextMuted   = Hex("78909C");
    static readonly Color BorderColor = Hex("2E4057");
    static readonly Color SliderFill  = Hex("0057A8");
    static readonly Color SliderBg    = Hex("0D1826");

    [MenuItem("Diakonie/Refactor UI")]
    static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != "Main")
        {
            Debug.LogWarning("[DiakonieUIRefactor] Bitte Main-Scene öffnen.");
            return;
        }

        // ── 1. ModelLoader aktivieren ─────────────────────────────────────
        var modelLoader = GameObject.Find("ModelLoader")
            ?? FindInactive("ModelLoader");
        if (modelLoader != null)
        {
            modelLoader.SetActive(true);
            Debug.Log("[DiakonieUIRefactor] ModelLoader aktiviert.");
        }
        else Debug.LogWarning("[DiakonieUIRefactor] ModelLoader nicht gefunden.");

        // ── 1b. CareDocumentationManager: medium-* Modellnamen setzen ────
        var manager = Object.FindFirstObjectByType<CareDocumentationManager>();
        if (manager != null)
        {
            manager.whisperEncoder = "medium-encoder.onnx";
            manager.whisperDecoder = "medium-decoder.onnx";
            manager.whisperTokens  = "medium-tokens.txt";
            EditorUtility.SetDirty(manager);
            Debug.Log("[DiakonieUIRefactor] Whisper-medium Modellnamen gesetzt.");
        }

        // ── 2. Doppeltes PanelFilePath entfernen ──────────────────────────
        var allPanels = Object.FindObjectsByType<GameObject>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int panelCount = 0;
        GameObject keepPanel = null;
        foreach (var go in allPanels)
        {
            if (go == null) continue; // destroyed in previous iteration
            if (go.name == "PanelFilePath" && go.transform.parent != null && go.transform.parent.name == "Canvas")
            {
                if (panelCount == 0) { keepPanel = go; panelCount++; }
                else { Object.DestroyImmediate(go); Debug.Log("[DiakonieUIRefactor] Doppeltes PanelFilePath entfernt."); }
            }
        }

        // ── 3. Canvas-Hintergrund ─────────────────────────────────────────
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            var canvasImg = canvas.GetComponent<Image>();
            if (canvasImg == null) canvasImg = canvas.gameObject.AddComponent<Image>();
            canvasImg.color = BG;

            // Header-Leiste oben
            EnsureHeader(canvas.transform);
        }

        // ── 4. Scroll View / Transkript ───────────────────────────────────
        StyleScrollView();

        // ── 5. WebhookPanel ───────────────────────────────────────────────
        StyleWebhookPanel();

        // ── 6. BasicButtonsPanel + Buttons ───────────────────────────────
        StyleButtonsPanel();

        // ── 7. Szene speichern ────────────────────────────────────────────
        var careDocUI = Object.FindFirstObjectByType<CareDocUI>();
        if (careDocUI != null) EditorUtility.SetDirty(careDocUI);
        if (modelLoader != null) EditorUtility.SetDirty(modelLoader);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        Debug.Log("[DiakonieUIRefactor] ✓ Fertig. Szene gespeichert.");
    }

    // ── Header ────────────────────────────────────────────────────────────

    static void EnsureHeader(Transform canvasT)
    {
        var existing = canvasT.Find("Header");
        if (existing != null) return;

        var header = new GameObject("Header");
        header.transform.SetParent(canvasT, false);
        header.transform.SetAsFirstSibling();

        var img = header.AddComponent<Image>();
        img.color = Accent;

        var rt = header.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = Vector2.one;
        rt.pivot     = new Vector2(0.5f, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.sizeDelta = new Vector2(0, 52);

        // Logo-Text links
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(header.transform, false);
        var title = titleGO.AddComponent<TextMeshProUGUI>();
        title.text = "DiakonieWhisper";
        title.fontSize = 22;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = new Vector2(20, 0);
        titleRT.offsetMax = new Vector2(-20, 0);

        // Subtitel rechts
        var subGO = new GameObject("Subtitle");
        subGO.transform.SetParent(header.transform, false);
        var sub = subGO.AddComponent<TextMeshProUGUI>();
        sub.text = "Bergische Diakonie · Sprachdokumentation";
        sub.fontSize = 12;
        sub.color = new Color(1, 1, 1, 0.7f);
        sub.alignment = TextAlignmentOptions.MidlineRight;
        var subRT = subGO.GetComponent<RectTransform>();
        subRT.anchorMin = Vector2.zero;
        subRT.anchorMax = Vector2.one;
        subRT.offsetMin = new Vector2(20, 0);
        subRT.offsetMax = new Vector2(-20, 0);
    }

    // ── Scroll View ───────────────────────────────────────────────────────

    static void StyleScrollView()
    {
        var sv = GameObject.Find("Scroll View");
        if (sv == null) return;

        var img = sv.GetComponent<Image>();
        if (img != null) img.color = InputBg;

        // Scrollbar ausblenden
        var scrollbar = sv.transform.Find("Scrollbar Vertical");
        if (scrollbar != null) scrollbar.gameObject.SetActive(false);

        // Transcript InputField
        var transkript = sv.transform.Find("Viewport/Content/Transkript");
        if (transkript == null) return;
        var field = transkript.GetComponent<TMP_InputField>();
        var fieldImg = transkript.GetComponent<Image>();
        if (fieldImg != null) fieldImg.color = InputBg;

        // Schrift
        if (field?.textComponent != null)
        {
            field.textComponent.fontSize = 15;
            field.textComponent.color = TextMain;
            field.textComponent.lineSpacing = 4;
        }
        if (field?.placeholder is TextMeshProUGUI ph)
        {
            ph.text = "Transkript erscheint hier während der Aufnahme...";
            ph.color = TextMuted;
        }
    }

    // ── WebhookPanel ──────────────────────────────────────────────────────

    static void StyleWebhookPanel()
    {
        var panel = GameObject.Find("WebhookPanel");
        if (panel == null) return;

        var img = panel.GetComponent<Image>();
        if (img != null) img.color = PanelBg;

        // URL-Label hinzufügen wenn nicht vorhanden
        EnsureLabelAbove(panel.transform, "WebhookURL", "Langdock Webhook-URL", 97072);
        EnsureLabelAbove(panel.transform, "WebhookSecret", "API-Schlüssel (optional)", 97136);

        StyleInputField(panel.transform.Find("WebhookURL")?.GetComponent<TMP_InputField>());
        StyleInputField(panel.transform.Find("WebhookSecret")?.GetComponent<TMP_InputField>());

        // Anzeige-Text (aktueller Webhook)
        var display = panel.transform.Find("WebhookURL (1)") ??
                      FindChildByInstanceID(panel.transform, 97232);
        if (display != null)
        {
            var t = display.GetComponent<TextMeshProUGUI>();
            if (t != null) { t.fontSize = 11; t.color = TextMuted; }
        }
    }

    static void EnsureLabelAbove(Transform parent, string targetName, string labelText, int skipID)
    {
        // Nur Style-Anpassung, kein neues Label nötig — Placeholder reicht
        var tf = parent.Find(targetName);
        if (tf == null) return;
        var field = tf.GetComponent<TMP_InputField>();
        if (field?.placeholder is TextMeshProUGUI ph)
        {
            ph.text = labelText;
            ph.color = TextMuted;
        }
        StyleInputField(field);
    }

    static void StyleInputField(TMP_InputField field)
    {
        if (field == null) return;
        var img = field.GetComponent<Image>();
        if (img != null) img.color = InputBg;
        if (field.textComponent != null)
        {
            field.textComponent.color = TextMain;
            field.textComponent.fontSize = 13;
        }
    }

    // ── Buttons Panel ─────────────────────────────────────────────────────

    static void StyleButtonsPanel()
    {
        var panel = GameObject.Find("BasicButtonsPanel");
        if (panel == null) return;

        var img = panel.GetComponent<Image>();
        if (img != null) img.color = PanelBg;

        StyleButton("BtnRecord",  RecordRed,   "● Aufnahme",    panel.transform, new Vector2(160, 52));
        StyleButton("BtnStop",    StopGrey,    "■ Stopp",       panel.transform, new Vector2(160, 52));
        StyleButton("BtnExport",  ExportGreen, "↑ Exportieren", panel.transform, new Vector2(160, 44));
        StyleButton("BtnUpload",  UploadBlue,  "↓ Audio laden", panel.transform, new Vector2(160, 44));

        // Status-Text
        var status = panel.transform.Find("Status");
        if (status != null)
        {
            var t = status.GetComponent<TextMeshProUGUI>();
            if (t != null) { t.color = TextMuted; t.fontSize = 13; }
        }

        // ProgressBar
        StyleProgressBar(panel.transform.Find("ProgresBar"));
    }

    static void StyleButton(string name, Color color, string label, Transform parent, Vector2 size)
    {
        var tf = parent.Find(name);
        if (tf == null) return;

        var img = tf.GetComponent<Image>();
        if (img != null) img.color = color;

        var rt = tf.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = size;

        var textTF = tf.Find("Text (TMP)");
        if (textTF != null)
        {
            var t = textTF.GetComponent<TextMeshProUGUI>();
            if (t != null)
            {
                t.text = label;
                t.color = Color.white;
                t.fontSize = 15;
                t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center;
            }
        }

        // Button ColorBlock
        var btn = tf.GetComponent<Button>();
        if (btn != null)
        {
            var cb = btn.colors;
            cb.normalColor      = color;
            cb.highlightedColor = Lighten(color, 0.15f);
            cb.pressedColor     = Darken(color, 0.15f);
            cb.selectedColor    = color;
            cb.disabledColor    = new Color(color.r, color.g, color.b, 0.35f);
            cb.colorMultiplier  = 1f;
            btn.colors = cb;
        }
    }

    static void StyleProgressBar(Transform pb)
    {
        if (pb == null) return;
        var bg = pb.Find("Background")?.GetComponent<Image>();
        if (bg != null) bg.color = SliderBg;
        var fill = pb.Find("Fill Area/Fill")?.GetComponent<Image>();
        if (fill != null) fill.color = SliderFill;
        var handle = pb.Find("Handle Slide Area/Handle")?.GetComponent<Image>();
        if (handle != null) handle.color = Lighten(SliderFill, 0.2f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    static Color Hex(string hex)
    {
        if (ColorUtility.TryParseHtmlString("#" + hex, out var c)) return c;
        return Color.white;
    }

    static Color Lighten(Color c, float amt) =>
        new Color(Mathf.Clamp01(c.r + amt), Mathf.Clamp01(c.g + amt), Mathf.Clamp01(c.b + amt), c.a);

    static Color Darken(Color c, float amt) =>
        new Color(Mathf.Clamp01(c.r - amt), Mathf.Clamp01(c.g - amt), Mathf.Clamp01(c.b - amt), c.a);

    static GameObject FindInactive(string name)
    {
        foreach (var go in Object.FindObjectsByType<GameObject>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (go.name == name) return go;
        return null;
    }

    static Transform FindChildByInstanceID(Transform parent, int id)
    {
        foreach (Transform t in parent)
            if (t.gameObject.GetInstanceID() == id) return t;
        return null;
    }
}
