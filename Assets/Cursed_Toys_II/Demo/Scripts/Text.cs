using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.InputSystem; // Yeni Input System için gerekli

namespace CursedToysII
{
    public class SimpleTextAligner : MonoBehaviour
    {
        [Header("Grid Layout Settings")]
        [Range(1, 4)]
        public int columns = 2;
        [Range(1, 6)]
        public int rows = 2;
        
        [Header("Positioning")]
        [Range(0f, 1f)]
        public float horizontalPosition = 0.5f;
        [Range(0f, 1f)]
        public float verticalPosition = 0.3f;
        
        [Header("Size & Spacing")]
        [Range(0.1f, 2.0f)]
        public float panelWidth = 0.6f;
        [Range(0.1f, 1.5f)]
        public float panelHeight = 0.4f;
        [Range(10f, 100f)]
        public float cellSpacing = 30f;
        
        [Header("Text Settings")]
        [Range(16f, 60f)]
        public float fontSize = 28f;
        public Color keyColor = Color.yellow;
        public Color descriptionColor = Color.white;
        public Color backgroundColor = new Color(0, 0, 0, 0.5f);
        
        [Header("Cinematic Effects")]
        [Range(0.5f, 3f)]
        public float fadeInDuration = 1.5f;
        [Range(3f, 10f)]
        public float displayDuration = 6f;
        [Range(0.5f, 3f)]
        public float fadeOutDuration = 1.5f;
        public bool useCinematicStyle = true;
        [Range(0.2f, 1.5f)]
        public float sequentialDelay = 0.6f;
        public bool autoShowOnStart = true;
        
        [Header("Text Content")]
        public string[] keyBindings = {"W A S D", "F", "Shift + F", "TAB"};
        public string[] descriptions = {"Movement", "Get Damage", "Death Anim", "Switch Character"};
        
        private GameObject panel;
        private Canvas canvas;
        private CanvasGroup panelCanvasGroup;
        private GameObject[] textElements;
        private bool isSetup = false;
        private bool isAnimating = false;
        
        // Input System için referanslar
        private Keyboard keyboard;
        
        void Start()
        {
            // Input System referansını al
            keyboard = Keyboard.current;
            
            SetupUI();
            if (autoShowOnStart)
            {
                StartCoroutine(ShowCinematicSequence());
            }
        }
        
        void SetupUI()
        {
            CreateCanvas();
            CreatePanel();
            CreateTextElements();
            isSetup = true;
            
            Debug.Log($"Cinematic Text Aligner created with {keyBindings.Length} elements");
        }
        
        void CreateCanvas()
        {
            canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("UI Canvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                
                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                
                canvasObj.AddComponent<GraphicRaycaster>();
                
                DontDestroyOnLoad(canvasObj);
            }
        }
        
        void CreatePanel()
        {
            if (panel != null)
            {
                DestroyImmediate(panel);
            }
            
            panel = new GameObject("CinematicTextPanel");
            panel.transform.SetParent(canvas.transform, false);
            
            panelCanvasGroup = panel.AddComponent<CanvasGroup>();
            panelCanvasGroup.alpha = 0f;
            
            Image bg = panel.AddComponent<Image>();
            bg.color = backgroundColor;
            bg.raycastTarget = false;
            
            UpdatePanelPosition();
        }
        
        void UpdatePanelPosition()
        {
            if (panel == null) return;
            
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            
            Vector2 anchorMin = new Vector2(horizontalPosition - panelWidth * 0.5f, verticalPosition - panelHeight * 0.5f);
            Vector2 anchorMax = new Vector2(horizontalPosition + panelWidth * 0.5f, verticalPosition + panelHeight * 0.5f);
            
            anchorMin = new Vector2(Mathf.Clamp01(anchorMin.x), Mathf.Clamp01(anchorMin.y));
            anchorMax = new Vector2(Mathf.Clamp01(anchorMax.x), Mathf.Clamp01(anchorMax.y));
            
            panelRect.anchorMin = anchorMin;
            panelRect.anchorMax = anchorMax;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            
            Debug.Log($"Panel positioned at anchors: {anchorMin} to {anchorMax}");
        }
        
        void CreateTextElements()
        {
            if (panel == null) return;
            
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(panel.transform.GetChild(i).gameObject);
            }
            
            int maxItems = Mathf.Min(keyBindings.Length, descriptions.Length);
            maxItems = Mathf.Min(maxItems, columns * rows);
            
            textElements = new GameObject[maxItems];
            
            for (int i = 0; i < maxItems; i++)
            {
                textElements[i] = CreateSingleTextElement(i);
                Debug.Log($"Created text element {i}: {textElements[i].name} at position {textElements[i].GetComponent<RectTransform>().anchorMin}");
            }
            
            Debug.Log($"Created {maxItems} text elements in {columns}x{rows} grid");
        }
        
        GameObject CreateSingleTextElement(int index)
        {
            GameObject textObj = new GameObject($"TextElement_{index}");
            textObj.transform.SetParent(panel.transform, false);
            
            CanvasGroup textCanvasGroup = textObj.AddComponent<CanvasGroup>();
            textCanvasGroup.alpha = 0f;
            
            bool tmpSuccess = false;
            
            try
            {
                TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();
                if (tmpText != null)
                {
                    string keyText = index < keyBindings.Length ? keyBindings[index] : "KEY";
                    string descText = index < descriptions.Length ? descriptions[index] : "Description";
                    
                    tmpText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(keyColor)}><b>{keyText}</b></color>\n<color=#{ColorUtility.ToHtmlStringRGB(descriptionColor)}>{descText}</color>";
                    tmpText.fontSize = fontSize;
                    tmpText.alignment = TextAlignmentOptions.Center;
                    tmpText.enableAutoSizing = false;
                    tmpText.raycastTarget = false;
                    tmpSuccess = true;
                    
                    Debug.Log($"TextMeshPro created for element {index}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"TextMeshPro failed for element {index}: {e.Message}");
            }
            
            if (!tmpSuccess)
            {
                Text regularText = textObj.AddComponent<Text>();
                string keyText = index < keyBindings.Length ? keyBindings[index] : "KEY";
                string descText = index < descriptions.Length ? descriptions[index] : "Description";
                
                regularText.text = $"{keyText}\n{descText}";
                regularText.fontSize = (int)fontSize;
                regularText.color = keyColor;
                regularText.alignment = TextAnchor.MiddleCenter;
                regularText.raycastTarget = false;
                
                Font arialFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (arialFont == null)
                    arialFont = Resources.FindObjectsOfTypeAll<Font>()[0];
                regularText.font = arialFont;
                
                Debug.Log($"Regular Text created for element {index}");
            }
            
            PositionTextElementFixed(textObj, index);
            
            return textObj;
        }
        
        void PositionTextElementFixed(GameObject textObj, int index)
        {
            RectTransform rect = textObj.GetComponent<RectTransform>();
            
            int row = index / columns;
            int col = index % columns;
            
            float cellWidth = 1f / columns;
            float cellHeight = 1f / rows;
            
            float padding = 0.05f;
            float actualCellWidth = cellWidth - padding;
            float actualCellHeight = cellHeight - padding;
            
            float xMin = col * cellWidth + padding * 0.5f;
            float xMax = xMin + actualCellWidth;
            float yMax = 1f - (row * cellHeight + padding * 0.5f);
            float yMin = yMax - actualCellHeight;
            
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, -index);
            
            Debug.Log($"Element {index} positioned at anchors: ({xMin:F2}, {yMin:F2}) to ({xMax:F2}, {yMax:F2})");
        }
        
        IEnumerator ShowCinematicSequence()
        {
            if (isAnimating) yield break;
            isAnimating = true;
            
            panel.SetActive(true);
            
            yield return new WaitForSeconds(0.3f);
            
            yield return StartCoroutine(FadeInPanel());
            
            if (useCinematicStyle)
            {
                for (int i = 0; i < textElements.Length; i++)
                {
                    if (textElements[i] != null)
                    {
                        StartCoroutine(FadeInTextElement(textElements[i].GetComponent<CanvasGroup>()));
                        yield return new WaitForSeconds(sequentialDelay);
                    }
                }
            }
            else
            {
                foreach (GameObject textElement in textElements)
                {
                    if (textElement != null)
                    {
                        StartCoroutine(FadeInTextElement(textElement.GetComponent<CanvasGroup>()));
                    }
                }
                yield return new WaitForSeconds(fadeInDuration);
            }
            
            yield return new WaitForSeconds(displayDuration);
            
            yield return StartCoroutine(FadeOutAll());
            
            panel.SetActive(false);
            isAnimating = false;
        }
        
        IEnumerator FadeInPanel()
        {
            float elapsedTime = 0f;
            float duration = fadeInDuration * 0.6f;
            
            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / duration;
                panelCanvasGroup.alpha = Mathf.Lerp(0f, 1f, progress);
                yield return null;
            }
            panelCanvasGroup.alpha = 1f;
        }
        
        IEnumerator FadeInTextElement(CanvasGroup textGroup)
        {
            if (textGroup == null) yield break;
            
            float elapsedTime = 0f;
            while (elapsedTime < fadeInDuration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / fadeInDuration;
                textGroup.alpha = Mathf.Lerp(0f, 1f, progress);
                yield return null;
            }
            textGroup.alpha = 1f;
        }
        
        IEnumerator FadeOutAll()
        {
            float elapsedTime = 0f;
            float startPanelAlpha = panelCanvasGroup.alpha;
            
            float[] initialAlphas = new float[textElements.Length];
            CanvasGroup[] textGroups = new CanvasGroup[textElements.Length];
            
            for (int i = 0; i < textElements.Length; i++)
            {
                if (textElements[i] != null)
                {
                    textGroups[i] = textElements[i].GetComponent<CanvasGroup>();
                    initialAlphas[i] = textGroups[i] != null ? textGroups[i].alpha : 0f;
                }
            }
            
            while (elapsedTime < fadeOutDuration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / fadeOutDuration;
                
                panelCanvasGroup.alpha = Mathf.Lerp(startPanelAlpha, 0f, progress);
                
                for (int i = 0; i < textElements.Length; i++)
                {
                    if (textGroups[i] != null)
                    {
                        textGroups[i].alpha = Mathf.Lerp(initialAlphas[i], 0f, progress);
                    }
                }
                
                yield return null;
            }
            
            panelCanvasGroup.alpha = 0f;
            foreach (var textGroup in textGroups)
            {
                if (textGroup != null)
                {
                    textGroup.alpha = 0f;
                }
            }
        }
        
        void OnValidate()
        {
            if (Application.isPlaying && isSetup)
            {
                UpdatePanelPosition();
                CreateTextElements();
            }
        }
        
        void Update()
        {
            // Keyboard referansını kontrol et (güvenlik için)
            if (keyboard == null)
                keyboard = Keyboard.current;
            
            // Keyboard yoksa (örneğin mobile'da) return et
            if (keyboard == null) return;
            
            // H tuşu - Cinematic sekansı göster
            if (keyboard.hKey.wasPressedThisFrame)
            {
                if (!isAnimating)
                {
                    StartCoroutine(ShowCinematicSequence());
                }
            }
            
            // T tuşu - Panel toggle
            if (keyboard.tKey.wasPressedThisFrame)
            {
                if (panel != null)
                {
                    panel.SetActive(!panel.activeInHierarchy);
                    
                    if (panel.activeInHierarchy)
                    {
                        panelCanvasGroup.alpha = 1f;
                        foreach (GameObject textElement in textElements)
                        {
                            if (textElement != null)
                            {
                                textElement.GetComponent<CanvasGroup>().alpha = 1f;
                            }
                        }
                    }
                }
            }
            
            // R tuşu - UI'yi yeniden setup et
            if (keyboard.rKey.wasPressedThisFrame)
            {
                SetupUI();
            }
            
            // I tuşu - Debug bilgileri
            if (keyboard.iKey.wasPressedThisFrame)
            {
                Debug.Log($"Active elements: {textElements?.Length ?? 0}");
                if (textElements != null)
                {
                    for (int i = 0; i < textElements.Length; i++)
                    {
                        if (textElements[i] != null)
                        {
                            RectTransform rt = textElements[i].GetComponent<RectTransform>();
                            Debug.Log($"Element {i}: Active={textElements[i].activeInHierarchy}, Anchors=({rt.anchorMin.x:F2},{rt.anchorMin.y:F2}) to ({rt.anchorMax.x:F2},{rt.anchorMax.y:F2})");
                        }
                    }
                }
            }
        }
    }
}