using UnityEngine;
using UnityEngine.UI;

namespace BergischeDiakonie.Speech
{
    /// <summary>
    /// Legt bei Awake ein frisches RenderTexture an, weist es der Camera zu
    /// und zeigt es im CharacterPanel RawImage an.
    /// Muss auf demselben GameObject wie die CharacterCamera liegen.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CharacterView : MonoBehaviour
    {
        [Header("RenderTexture")]
        public int rtWidth  = 256;
        public int rtHeight = 512;

        [Header("UI")]
        public RawImage targetImage;

        Camera _cam;

        void Awake()
        {
            _cam = GetComponent<Camera>();

            var rt = new RenderTexture(rtWidth, rtHeight, 16, RenderTextureFormat.ARGB32);
            rt.name = "CharacterRT_Runtime";
            rt.Create();

            _cam.targetTexture = rt;

            // Auto-find RawImage if not assigned
            if (targetImage == null)
            {
                var panel = GameObject.Find("CharacterPanel");
                if (panel != null) targetImage = panel.GetComponent<RawImage>();
            }

            if (targetImage != null)
                targetImage.texture = rt;
            else
                Debug.LogWarning("[CharacterView] CharacterPanel/RawImage nicht gefunden.");
        }

        void OnDestroy()
        {
            if (_cam != null && _cam.targetTexture != null)
            {
                _cam.targetTexture.Release();
                Destroy(_cam.targetTexture);
            }
        }
    }
}
