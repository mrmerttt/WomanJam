using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Böbrek satıldıktan sonra ekranın üstüne eklenen huzursuz edici katman:
/// kenarlarda yavaşça nefes alan kararmalar, ara ara beliren yavaş piksel hataları ve nadir titreşimler.
/// Kendi Canvas'ında çalışır, tıklamaları engellemez. BlackjackManager'daki StopAllCoroutines'ten etkilenmez.
/// </summary>
public class KidneyCurseEffect : MonoBehaviour
{
    [Header("Kenar Kararması")]
    [Range(0f, 1f)] public float kararmaGucu = 0.85f;
    [Tooltip("Kararmanın nefes alıp verme hızı (düşük = daha yavaş)")]
    public float kararmaHizi = 0.15f;

    [Header("Piksel Hataları")]
    [Tooltip("Saniyede ortalama kaç yeni piksel hatası belirsin")]
    public float pikselHataSikligi = 0.7f;
    public float pikselBoyutu = 8f;

    [Header("Nadir Titreşim")]
    [Tooltip("İki titreşim arasındaki rastgele bekleme süresi (saniye)")]
    public Vector2 titresimAraligi = new Vector2(12f, 28f);
    public float titresimSuresi = 0.4f;
    public float titresimGucu = 8f;

    private const int GlitchPoolSize = 16;
    private const float StepTime = 1f / 12f; // Retro, düşük FPS'li adımlar

    private class Glitch
    {
        public RectTransform rt;
        public Image img;
        public Color color;
        public float startTime;
        public float life;
        public bool active;
    }

    private static readonly Color[] glitchColors =
    {
        new Color(0f, 0f, 0f, 1f),
        new Color(0.05f, 0.02f, 0.04f, 1f),
        new Color(0.55f, 0.05f, 0.10f, 1f),
        new Color(0.85f, 0.10f, 0.75f, 1f),
        new Color(0.10f, 0.90f, 0.60f, 1f),
        new Color(0.90f, 0.90f, 0.85f, 1f),
    };

    private RectTransform shakeTarget;
    private Vector2 shakeOrigin;
    private bool isShaking;
    private float shakeEndTime;
    private float nextShakeTime;
    private float nextStepTime;

    private Image vignette;
    private Glitch[] glitches;
    private float noiseSeed;

    public static KidneyCurseEffect Create(RectTransform shakeTarget)
    {
        GameObject obj = new GameObject("KidneyCurseOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

        Canvas canvas = obj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // Her şeyin üstünde

        CanvasScaler scaler = obj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        KidneyCurseEffect fx = obj.AddComponent<KidneyCurseEffect>();
        fx.shakeTarget = shakeTarget;
        return fx;
    }

    private void Awake()
    {
        noiseSeed = Random.Range(0f, 100f);

        vignette = CreateLayer("EdgeDarkness");
        vignette.rectTransform.anchorMin = Vector2.zero;
        vignette.rectTransform.anchorMax = Vector2.one;
        vignette.rectTransform.offsetMin = Vector2.zero;
        vignette.rectTransform.offsetMax = Vector2.zero;
        vignette.sprite = CreateVignetteSprite();
        vignette.color = new Color(1f, 1f, 1f, 0f);

        glitches = new Glitch[GlitchPoolSize];
        for (int i = 0; i < GlitchPoolSize; i++)
        {
            Image img = CreateLayer($"PixelGlitch_{i}");
            img.gameObject.SetActive(false);
            glitches[i] = new Glitch { rt = img.rectTransform, img = img };
        }

        nextShakeTime = Time.time + Random.Range(titresimAraligi.x, titresimAraligi.y);
    }

    private void Update()
    {
        float now = Time.time;
        if (now < nextStepTime) return;
        nextStepTime = now + StepTime;

        UpdateEdgeDarkness(now);
        UpdateGlitches(now);
        UpdateShake(now);
    }

    private void OnDisable()
    {
        if (isShaking && shakeTarget != null)
            shakeTarget.anchoredPosition = shakeOrigin;
        isShaking = false;
    }

    private void UpdateEdgeDarkness(float now)
    {
        // Perlin gürültüsü ile yavaşça koyulaşıp açılan kenarlar, titreşim anında bir anlık daha karanlık
        float n = Mathf.PerlinNoise(now * kararmaHizi, noiseSeed);
        float alpha = Mathf.Lerp(0.3f, 1f, n) * kararmaGucu;
        if (isShaking) alpha = Mathf.Min(1f, alpha + 0.25f);

        vignette.color = new Color(1f, 1f, 1f, alpha);
    }

    private void UpdateGlitches(float now)
    {
        foreach (Glitch g in glitches)
        {
            if (!g.active) continue;

            float t = (now - g.startTime) / g.life;
            if (t >= 1f)
            {
                g.active = false;
                g.img.gameObject.SetActive(false);
                continue;
            }

            // Yavaşça belirip kaybolur, kademeli (basamaklı) alfa ile pixel hissi verir
            float fade = Mathf.Round(Mathf.Sin(t * Mathf.PI) * 4f) / 4f;
            g.img.color = new Color(g.color.r, g.color.g, g.color.b, g.color.a * fade);

            // Ara sıra yatayda bir piksel kayar
            if (Random.value < 0.1f)
                g.rt.anchoredPosition += new Vector2(Random.value < 0.5f ? -pikselBoyutu : pikselBoyutu, 0f);
        }

        float spawnChance = pikselHataSikligi * StepTime;
        if (isShaking) spawnChance = 0.5f;
        if (Random.value < spawnChance)
            SpawnGlitch(now);
    }

    private void SpawnGlitch(float now)
    {
        foreach (Glitch g in glitches)
        {
            if (g.active) continue;

            Vector2 anchor = new Vector2(Random.value, Random.value);
            g.rt.anchorMin = anchor;
            g.rt.anchorMax = anchor;
            g.rt.anchoredPosition = Vector2.zero;
            g.rt.sizeDelta = new Vector2(pikselBoyutu * Random.Range(1, 13), pikselBoyutu * Random.Range(1, 4));

            Color c = glitchColors[Random.Range(0, glitchColors.Length)];
            c.a = Random.Range(0.3f, 0.75f);
            g.color = c;
            g.img.color = new Color(c.r, c.g, c.b, 0f);

            g.startTime = now;
            g.life = Random.Range(0.8f, 2.6f);
            g.active = true;
            g.img.gameObject.SetActive(true);
            return;
        }
    }

    private void UpdateShake(float now)
    {
        if (shakeTarget == null) return;

        if (!isShaking && now >= nextShakeTime)
        {
            isShaking = true;
            shakeOrigin = shakeTarget.anchoredPosition;
            shakeEndTime = now + titresimSuresi;
        }

        if (!isShaking) return;

        if (now >= shakeEndTime)
        {
            shakeTarget.anchoredPosition = shakeOrigin;
            isShaking = false;
            nextShakeTime = now + Random.Range(titresimAraligi.x, titresimAraligi.y);
            return;
        }

        float strength = titresimGucu * ((shakeEndTime - now) / titresimSuresi);
        Vector2 offset = Random.insideUnitCircle * strength;
        shakeTarget.anchoredPosition = shakeOrigin + new Vector2(Mathf.Round(offset.x), Mathf.Round(offset.y));
    }

    private Image CreateLayer(string layerName)
    {
        GameObject obj = new GameObject(layerName, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(transform, false);
        Image img = obj.GetComponent<Image>();
        img.raycastTarget = false; // Altındaki butonlara tıklamayı engellemesin
        return img;
    }

    private static Sprite CreateVignetteSprite()
    {
        // Düşük çözünürlüklü + Point filtre: kenar kararması da hafif pixelli görünür
        const int w = 96;
        const int h = 54;

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float v = (y + 0.5f) / h * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v) / 1.4142f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.95f, d));
                pixels[y * w + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }
}
