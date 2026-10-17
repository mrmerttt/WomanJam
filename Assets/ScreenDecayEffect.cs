using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Oyun boyunca ekranın üstünde duran huzursuz edici katman:
/// kenarlarda yavaşça nefes alan kararmalar, ara ara beliren yavaş piksel hataları ve titreşimler.
/// En başta çok hafiftir; her eşya satıldığında SetLevel ile bir tık güçlenir.
/// Kendi Canvas'ında çalışır, tıklamaları engellemez. BlackjackManager'daki StopAllCoroutines'ten etkilenmez.
/// </summary>
public class ScreenDecayEffect : MonoBehaviour
{
    [Header("Seviye Aralıkları (X = en baştaki hali, Y = her şey satıldığındaki hali)")]
    [Tooltip("Kenar kararmasının gücü")]
    public Vector2 kararmaGucu = new Vector2(0.08f, 0.85f);

    [Tooltip("Saniyede ortalama kaç yeni piksel hatası belirsin")]
    public Vector2 pikselHataSikligi = new Vector2(0.06f, 1.1f);

    [Tooltip("En baştaki iki titreşim arası rastgele bekleme (saniye)")]
    public Vector2 baslangicTitresimAraligi = new Vector2(45f, 75f);

    [Tooltip("Her şey satıldığındaki iki titreşim arası rastgele bekleme (saniye)")]
    public Vector2 sonTitresimAraligi = new Vector2(6f, 12f);

    [Tooltip("Titreşimin şiddeti (piksel)")]
    public Vector2 titresimGucu = new Vector2(2f, 14f);

    [Tooltip("Titreşimin süresi (saniye)")]
    public Vector2 titresimSuresi = new Vector2(0.2f, 0.5f);

    [Header("Diğer")]
    [Tooltip("Kararmanın nefes alıp verme hızı (düşük = daha yavaş)")]
    public float kararmaHizi = 0.15f;
    public float pikselBoyutu = 8f;

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
    private float shakeStartTime;
    private float shakeEndTime;
    private float shakeStrength;
    private float nextShakeTime;
    private float nextStepTime;

    private float level01; // 0 = en hafif, 1 = en güçlü

    private Image vignette;
    private Glitch[] glitches;
    private float noiseSeed;

    public static ScreenDecayEffect Create(RectTransform shakeTarget)
    {
        GameObject obj = new GameObject("ScreenDecayOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

        Canvas canvas = obj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // Her şeyin üstünde

        CanvasScaler scaler = obj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        ScreenDecayEffect fx = obj.AddComponent<ScreenDecayEffect>();
        fx.shakeTarget = shakeTarget;
        return fx;
    }

    /// <summary>
    /// Efektin şiddetini ayarlar. level = satılan eşya sayısı, maxLevel = toplam eşya sayısı.
    /// </summary>
    public void SetLevel(int level, int maxLevel)
    {
        level01 = maxLevel > 0 ? Mathf.Clamp01((float)level / maxLevel) : 0f;

        // Yeni seviyede titreşimler daha sık olmalı; bekleyen titreşim çok uzaktaysa öne çek
        float soonest = Time.time + RandomShakeInterval();
        if (!isShaking && nextShakeTime > soonest)
            nextShakeTime = soonest;
    }

    /// <summary>
    /// Sıradan bağımsız, hemen bir titreşim başlatır (örneğin bir eşya satılınca veya ölüm sonunda).
    /// </summary>
    public void TriggerShake(float duration, float strength)
    {
        if (shakeTarget == null) return;

        if (!isShaking)
            shakeOrigin = shakeTarget.anchoredPosition;

        isShaking = true;
        shakeStartTime = Time.time;
        shakeEndTime = Time.time + Mathf.Max(0.05f, duration);
        shakeStrength = strength;
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

        nextShakeTime = Time.time + RandomShakeInterval();
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

    private float Level(Vector2 range) => Mathf.Lerp(range.x, range.y, level01);

    private float RandomShakeInterval()
    {
        float min = Mathf.Lerp(baslangicTitresimAraligi.x, sonTitresimAraligi.x, level01);
        float max = Mathf.Lerp(baslangicTitresimAraligi.y, sonTitresimAraligi.y, level01);
        return Random.Range(min, max);
    }

    private void UpdateEdgeDarkness(float now)
    {
        // Perlin gürültüsü ile yavaşça koyulaşıp açılan kenarlar, titreşim anında bir anlık daha karanlık
        float n = Mathf.PerlinNoise(now * kararmaHizi, noiseSeed);
        float alpha = Mathf.Lerp(0.3f, 1f, n) * Level(kararmaGucu);
        if (isShaking) alpha = Mathf.Min(1f, alpha + 0.2f);

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

        float spawnChance = Level(pikselHataSikligi) * StepTime;
        if (isShaking) spawnChance = Mathf.Max(spawnChance, 0.35f);
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

            // Seviye arttıkça hatalar biraz daha büyük olabilir
            int maxWidth = Mathf.RoundToInt(Mathf.Lerp(5f, 13f, level01));
            g.rt.sizeDelta = new Vector2(pikselBoyutu * Random.Range(1, maxWidth), pikselBoyutu * Random.Range(1, 4));

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
            TriggerShake(Level(titresimSuresi), Level(titresimGucu));

        if (!isShaking) return;

        if (now >= shakeEndTime)
        {
            shakeTarget.anchoredPosition = shakeOrigin;
            isShaking = false;
            nextShakeTime = now + RandomShakeInterval();
            return;
        }

        float remaining = (shakeEndTime - now) / Mathf.Max(0.05f, shakeEndTime - shakeStartTime);
        Vector2 offset = Random.insideUnitCircle * shakeStrength * remaining;
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
