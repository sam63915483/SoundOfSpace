using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    public Image fillImage;

    static Sprite s_blankSprite;
    Camera cam;
    bool _spritesAssigned;

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void SetFill(float pct01)
    {
        if (fillImage == null) return;
        // The prefab's images have no sprite, so Image.Type=Filled won't render
        // fillAmount changes. Assign a 1x1 white sprite the first time we draw.
        EnsureSpritesAssigned();
        fillImage.fillAmount = Mathf.Clamp01(pct01);
    }

    void EnsureSpritesAssigned()
    {
        if (_spritesAssigned) return;
        _spritesAssigned = true;
        var images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i].sprite == null) images[i].sprite = GetBlankSprite();
        }
    }

    static Sprite GetBlankSprite()
    {
        if (s_blankSprite == null)
        {
            var tex = Texture2D.whiteTexture;
            s_blankSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                          new Vector2(0.5f, 0.5f), 100f);
        }
        return s_blankSprite;
    }

    void LateUpdate()
    {
        if (cam == null)
        {
            var pc = FindObjectOfType<PlayerController>(true);
            if (pc != null) cam = pc.Camera;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
        }

        // Match camera rotation so the canvas always faces the viewer head-on.
        transform.rotation = cam.transform.rotation;
    }
}
