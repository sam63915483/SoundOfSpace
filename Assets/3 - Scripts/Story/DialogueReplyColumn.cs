using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A vertical list of preset reply buttons anchored to the RIGHT of the phone chassis.
/// Purely a view: handed a list of button labels + a click callback. Built procedurally
/// to match the project's code-built-UI house style. Follows the chassis each frame so it
/// tracks the phone's slide animation.
/// </summary>
public class DialogueReplyColumn : MonoBehaviour
{
    RectTransform _root;
    RectTransform _chassis;
    readonly List<GameObject> _buttons = new List<GameObject>();

    static readonly Color BtnBg      = new Color(0.10f, 0.13f, 0.20f, 0.95f);
    static readonly Color BtnBgHover = new Color(0.16f, 0.22f, 0.32f, 0.98f);
    static readonly Color BtnText    = new Color(0.85f, 0.95f, 1f, 1f);

    /// <param name="phoneCanvas">the phone's Canvas transform</param>
    /// <param name="phoneChassis">the phone chassis RectTransform to anchor beside (may be null)</param>
    public static DialogueReplyColumn Create(Transform phoneCanvas, RectTransform phoneChassis)
    {
        var go = new GameObject("DialogueReplyColumn", typeof(RectTransform));
        go.transform.SetParent(phoneCanvas, false);
        var col = go.AddComponent<DialogueReplyColumn>();
        col.Build(phoneChassis);
        return col;
    }

    void Build(RectTransform chassis)
    {
        _root = (RectTransform)transform;
        _chassis = chassis;
        _root.sizeDelta = new Vector2(300f, 0f);
        _root.pivot = new Vector2(0f, 1f);   // top-left pivot, grows downward

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (_chassis == null) return;
        // Follow the right edge of the phone chassis, near its top.
        Vector3 rightEdgeTop = _chassis.TransformPoint(new Vector3(_chassis.rect.xMax, _chassis.rect.yMax, 0f));
        _root.position = rightEdgeTop + new Vector3(24f, 0f, 0f);
    }

    public void Show(List<string> labels, Action<int> onPick)
    {
        Clear();
        for (int i = 0; i < labels.Count; i++)
        {
            int idx = i;
            _buttons.Add(MakeButton(labels[i], () => onPick?.Invoke(idx)));
        }
        gameObject.SetActive(labels.Count > 0);
    }

    public void Clear()
    {
        foreach (var b in _buttons) if (b != null) Destroy(b);
        _buttons.Clear();
        gameObject.SetActive(false);
    }

    GameObject MakeButton(string label, Action onClick)
    {
        var go = new GameObject("Reply", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>(); img.color = BtnBg;
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = BtnBg; colors.highlightedColor = BtnBgHover; colors.pressedColor = BtnBgHover;
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick());
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 44f;

        var txtGo = new GameObject("Label", typeof(RectTransform));
        txtGo.transform.SetParent(go.transform, false);
        var rt = (RectTransform)txtGo.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(12f, 6f); rt.offsetMax = new Vector2(-12f, -6f);
        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = 18f; tmp.color = BtnText;
        tmp.enableWordWrapping = true; tmp.alignment = TextAlignmentOptions.Left;
        return go;
    }
}
