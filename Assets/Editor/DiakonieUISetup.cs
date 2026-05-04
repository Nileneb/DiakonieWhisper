using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using BergischeDiakonie.Speech;

/// <summary>
/// Einmaliges Setup-Script: Diakonie > Setup UI ausführen.
/// Konvertiert Transkript TMP_Text → TMP_InputField,
/// fügt Upload-Button + Datei-Pfad-Panel hinzu, verdrahtet CareDocUI.
/// </summary>
public static class DiakonieUISetup
{
    [MenuItem("Diakonie/Setup UI")]
    static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != "Main")
        {
            Debug.LogWarning("[DiakonieUISetup] Bitte Main-Scene öffnen.");
            return;
        }

        var careDocUI = Object.FindFirstObjectByType<CareDocUI>();
        if (careDocUI == null)
        {
            Debug.LogError("[DiakonieUISetup] CareDocUI nicht gefunden.");
            return;
        }

        // ── 1. Transkript TMP_Text → TMP_InputField ───────────────────────
        var transkriptGO = GameObject.Find("Transkript");
        TMP_InputField transcriptField = null;

        if (transkriptGO != null)
        {
            var oldText = transkriptGO.GetComponent<TextMeshProUGUI>();
            var contentRT = transkriptGO.transform.parent?.GetComponent<RectTransform>();

            // Create new InputField in same parent
            transcriptField = CreateInputField(
                "Transkript",
                transkriptGO.transform.parent,
                contentRT != null ? contentRT.rect.size : new Vector2(800, 400));

            // Copy position/size from old object
            var newRT = transcriptField.GetComponent<RectTransform>();
            var oldRT = transkriptGO.GetComponent<RectTransform>();
            if (oldRT != null)
            {
                newRT.anchorMin = oldRT.anchorMin;
                newRT.anchorMax = oldRT.anchorMax;
                newRT.offsetMin = oldRT.offsetMin;
                newRT.offsetMax = oldRT.offsetMax;
                newRT.pivot     = oldRT.pivot;
                newRT.anchoredPosition = oldRT.anchoredPosition;
                newRT.sizeDelta = oldRT.sizeDelta;
            }

            // Swap sibling index so it sits in the same slot
            transcriptField.transform.SetSiblingIndex(transkriptGO.transform.GetSiblingIndex());
            Object.DestroyImmediate(transkriptGO);
            Debug.Log("[DiakonieUISetup] Transkript TMP_Text → TMP_InputField.");
        }
        else
        {
            // Already replaced or missing — try to find existing TMP_InputField named Transkript
            var existing = GameObject.Find("Transkript")?.GetComponent<TMP_InputField>();
            if (existing != null) transcriptField = existing;
            else Debug.LogWarning("[DiakonieUISetup] Transkript-Objekt nicht gefunden.");
        }

        // ── 2. Upload-Button + LLM-Button in BasicButtonsPanel ───────────────
        var buttonsPanel = GameObject.Find("BasicButtonsPanel");
        Button uploadBtn = null;
        Button llmBtn = null;
        if (buttonsPanel != null)
        {
            // BtnUpload: nur anlegen falls nicht vorhanden
            var existingUpload = buttonsPanel.transform.Find("BtnUpload");
            if (existingUpload != null)
                uploadBtn = existingUpload.GetComponent<Button>();
            else
            {
                uploadBtn = CreateButton("BtnUpload", "↓ Audio laden", buttonsPanel.transform);
                uploadBtn.transform.SetAsLastSibling();
                Debug.Log("[DiakonieUISetup] BtnUpload hinzugefügt.");
            }

            // BtnLLM: nur anlegen falls nicht vorhanden
            var existingLLM = buttonsPanel.transform.Find("BtnLLM");
            if (existingLLM != null)
                llmBtn = existingLLM.GetComponent<Button>();
            else
            {
                llmBtn = CreateButton("BtnLLM", "LLM verarbeiten", buttonsPanel.transform);
                // LLM-Button in dunkelviolett
                llmBtn.GetComponent<UnityEngine.UI.Image>().color = new Color(0.35f, 0.1f, 0.5f, 1f);
                llmBtn.transform.SetAsLastSibling();
                Debug.Log("[DiakonieUISetup] BtnLLM hinzugefügt.");
            }
        }
        else
        {
            Debug.LogWarning("[DiakonieUISetup] BasicButtonsPanel nicht gefunden.");
        }

        // ── 3. PanelFilePath (Overlay-Panel für Pfad-Eingabe) ─────────────
        var canvas = GameObject.Find("Canvas")?.GetComponent<Canvas>();
        if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>();
        GameObject panelFilePath = null;
        TMP_InputField inputFilePath = null;

        if (canvas != null)
        {
            panelFilePath = new GameObject("PanelFilePath");
            panelFilePath.transform.SetParent(canvas.transform, false);
            panelFilePath.SetActive(false);

            var panelImg = panelFilePath.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

            var panelRT = panelFilePath.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.1f, 0.35f);
            panelRT.anchorMax = new Vector2(0.9f, 0.65f);
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            // Label
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(panelFilePath.transform, false);
            var labelTxt = labelGO.AddComponent<TextMeshProUGUI>();
            labelTxt.text = "WAV-Dateipfad eingeben:";
            labelTxt.fontSize = 18;
            labelTxt.alignment = TextAlignmentOptions.Center;
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0.05f, 0.65f);
            labelRT.anchorMax = new Vector2(0.95f, 0.90f);
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            // Path InputField
            inputFilePath = CreateInputField("InputFilePath", panelFilePath.transform, new Vector2(600, 40));
            var pathRT = inputFilePath.GetComponent<RectTransform>();
            pathRT.anchorMin = new Vector2(0.05f, 0.40f);
            pathRT.anchorMax = new Vector2(0.95f, 0.65f);
            pathRT.offsetMin = Vector2.zero;
            pathRT.offsetMax = Vector2.zero;
            inputFilePath.lineType = TMP_InputField.LineType.SingleLine;
            var ph = inputFilePath.placeholder as TextMeshProUGUI;
            if (ph != null) ph.text = "/pfad/zur/datei.wav";

            // Confirm button
            var confirmBtn = CreateButton("BtnConfirmLoad", "Laden", panelFilePath.transform);
            var confirmRT = confirmBtn.GetComponent<RectTransform>();
            confirmRT.anchorMin = new Vector2(0.30f, 0.10f);
            confirmRT.anchorMax = new Vector2(0.70f, 0.35f);
            confirmRT.offsetMin = Vector2.zero;
            confirmRT.offsetMax = Vector2.zero;

            // Wire confirm button → CareDocUI.LoadFromInputPath()
            confirmBtn.onClick.AddListener(careDocUI.LoadFromInputPath);

            Debug.Log("[DiakonieUISetup] PanelFilePath erstellt.");
        }

        // ── 4. CareDocUI verdrahten ────────────────────────────────────────
        if (transcriptField != null)
            careDocUI.inputTranscript = transcriptField;

        if (uploadBtn != null)
            careDocUI.btnUpload = uploadBtn;

        if (llmBtn != null)
            careDocUI.btnLLM = llmBtn;

        if (panelFilePath != null)
            careDocUI.panelFilePath = panelFilePath;

        if (inputFilePath != null)
            careDocUI.inputFilePath = inputFilePath;

        EditorUtility.SetDirty(careDocUI);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

        Debug.Log("[DiakonieUISetup] ✓ UI-Setup abgeschlossen. Szene gespeichert.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static TMP_InputField CreateInputField(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.15f, 1f);

        var field = go.AddComponent<TMP_InputField>();

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        // Text Area
        var textAreaGO = new GameObject("Text Area");
        textAreaGO.transform.SetParent(go.transform, false);
        var textAreaRT = textAreaGO.AddComponent<RectTransform>();
        textAreaRT.anchorMin = Vector2.zero;
        textAreaRT.anchorMax = Vector2.one;
        textAreaRT.offsetMin = new Vector2(4, 4);
        textAreaRT.offsetMax = new Vector2(-4, -4);
        textAreaGO.AddComponent<RectMask2D>();

        // Placeholder
        var placeholderGO = new GameObject("Placeholder");
        placeholderGO.transform.SetParent(textAreaGO.transform, false);
        var phRT = placeholderGO.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero;
        phRT.offsetMax = Vector2.zero;
        var phText = placeholderGO.AddComponent<TextMeshProUGUI>();
        phText.text = "";
        phText.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
        phText.fontSize = 16;
        phText.textWrappingMode = TextWrappingModes.Normal;

        // Text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(textAreaGO.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        var textComp = textGO.AddComponent<TextMeshProUGUI>();
        textComp.color = Color.white;
        textComp.fontSize = 16;
        textComp.textWrappingMode = TextWrappingModes.Normal;

        // Wire InputField
        field.textViewport = textAreaRT;
        field.textComponent = textComp;
        field.placeholder = phText;
        field.lineType = TMP_InputField.LineType.MultiLineNewline;
        field.interactable = true;

        return field;
    }

    static Button CreateButton(string name, string label, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.4f, 0.8f, 1f);

        var btn = go.AddComponent<Button>();
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160, 40);

        var labelGO = new GameObject("Text (TMP)");
        labelGO.transform.SetParent(go.transform, false);
        var lbl = labelGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 16;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.color = Color.white;
        var lblRT = labelGO.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;

        var nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;

        return btn;
    }
}
