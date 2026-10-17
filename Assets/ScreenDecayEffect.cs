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
    public Vector2 pikselHataSikligi = new Vector2(0.15f, 7f);

    [Tooltip("Bir piksel hatasının en fazla kaç piksel-blok genişliğinde olabileceği (kapladığı alan)")]
    public Vector2 pikselHataGenisligi = new Vector2(4f, 40f);

    [Tooltip("Bir piksel hatasının en fazla kaç piksel-blok yüksekliğinde olabileceği (kapladığı alan)")]
    public Vector2 pikselHataYuksekligi = new Vector2(2f, 10f);

    [Tooltip("Piksel hatalarının ekranda kayma sıklığı (her adımda kayma ihtimali) - yüksek = daha hızlı/huzursuz")]
    public Vector2 pikselKaymaSikligi = new Vector2(0.08f, 0.6f);

    [Tooltip("Bir piksel hatasının ekranda kalma süresi (saniye). Seviye arttıkça hatalar daha hızlı gelip gider.")]
    public Vector2 pikselHataOmru = new Vector2(2.2f, 0.6f);

    [Tooltip("En baştaki iki titreşim arası rastgele bekleme (saniye)")]
    public Vector2 baslangicTitresimAraligi = new Vector2(45f, 75f);

    [Tooltip("Her şey satıldığındaki iki titreşim arası rastgele bekleme (saniye)")]
    public Vector2 sonTitresimAraligi = new Vector2(4f, 8f);

    [Tooltip("Titreşimin şiddeti (piksel). Titreşimler özellikle sona doğru artar.")]
    public Vector2 titresimGucu = new Vector2(2f, 16f);

    [Tooltip("Titreşimin süresi (saniye)")]
    public Vector2 titresimSuresi = new Vector2(0.2f, 0.5f);

    [Header("Boydan Boya Şerit Hataları")]
    [Tooltip("Yeni hataların ne kadarı şerit olsun (0-1). Yarısı yukarıdan aşağı kayan yatay şerit (ekranı soldan sağa kaplar), yarısı sağdan sola kayan dikey şerit (ekranı yukarıdan aşağı kaplar).")]
    public Vector2 seritOrani = new Vector2(0.2f, 0.45f);

    [Tooltip("Şeritlerin kayma hızı (piksel/saniye)")]
    public Vector2 seritHizi = new Vector2(80f, 480f);

    [Tooltip("Şeritlerin en fazla kalınlığı (piksel-blok)")]
    public Vector2 seritKalinligi = new Vector2(1f, 5f);

    [Tooltip("Şeritler boydan boya uzandığı için opaklıklarını bu oranla düşür")]
    [Range(0f, 1f)] public float seritOpaklikCarpani = 0.6f;

    [Header("Diğer")]
    [Tooltip("Kararmanın nefes alıp verme hızı (düşük = daha yavaş)")]
    public float kararmaHizi = 0.15f;
    public float pikselBoyutu = 8f;

    private const int GlitchPoolSize = 64;
    private const float StepTime = 1f / 12f; // Retro, düşük FPS'li adımlar

    private enum GlitchKind { Block, HorizontalStrip, VerticalStrip }

    private class Glitch
    {
        public RectTransform rt;
        public Image img;
        public Color color;
        public float startTime;
        public float life;
        public bool active;
        public GlitchKind kind;
        public Vector2 position; // Şeritlerin kayarken tuttuğu gerçek konum (ekrana piksel adımlarıyla yansır)
        public Vector2 velocity;
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
    /// Titreşimin uygulanacağı ekranı değiştirir (örneğin satış ekranı açıkken o ekran sarsılır).
    /// </summary>
    public void SetShakeTarget(RectTransform target)
    {
        if (target == shakeTarget) return;

        if (isShaking && shakeTarget != null)
            shakeTarget.anchoredPosition = shakeOrigin;

        shakeTarget = target;

        if (isShaking && shakeTarget != null)
            shakeOrigin = shakeTarget.anchoredPosition;
        else
            isShaking = false;
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

    // Titreşimler ilk satışlarda az değişir, sona doğru hızla artar
    private float ShakeLevel01 => level01 * level01;
    private float ShakeLevel(Vector2 range) => Mathf.Lerp(range.x, range.y, ShakeLevel01);

    private float RandomShakeInterval()
    {
        float min = Mathf.Lerp(baslangicTitresimAraligi.x, sonTitresimAraligi.x, ShakeLevel01);
        float max = Mathf.Lerp(baslangicTitresimAraligi.y, sonTitresimAraligi.y, ShakeLevel01);
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

            if (g.kind == GlitchKind.Block)
            {
                // Ara sıra yatayda kayar; seviye arttıkça daha sık ve daha uzağa
                if (Random.value < Level(pikselKaymaSikligi))
                {
                    float jump = pikselBoyutu * Random.Range(1, 1 + Mathf.RoundToInt(Mathf.Lerp(1f, 4f, level01)));
                    g.rt.anchoredPosition += new Vector2(Random.value < 0.5f ? -jump : jump, 0f);
                }
            }
            else
            {
                // Şeritler sabit hızla kayar, ekrana piksel adımlarıyla yansır
                g.position += g.velocity * StepTime;
                g.rt.anchoredPosition = new Vector2(
                    Mathf.Round(g.position.x / pikselBoyutu) * pikselBoyutu,
                    Mathf.Round(g.position.y / pikselBoyutu) * pikselBoyutu);
            }
        }

        // Saniyedeki hata sayısı adım başına beklenen sayıya çevrilir; yüksek seviyede bir adımda birden fazla hata doğabilir
        float expected = Level(pikselHataSikligi) * StepTime;
        if (isShaking) expected = Mathf.Max(expected, 0.35f) * 1.5f;

        int spawnCount = Mathf.FloorToInt(expected);
        if (Random.value < expected - spawnCount) spawnCount++;
        for (int i = 0; i < spawnCount; i++)
            SpawnGlitch(now);
    }

    private void SpawnGlitch(float now)
    {
        foreach (Glitch g in glitches)
        {
            if (g.active) continue;

            Color c = glitchColors[Random.Range(0, glitchColors.Length)];
            c.a = Random.Range(0.3f, 0.75f);

            g.position = Vector2.zero;
            g.velocity = Vector2.zero;
            g.rt.anchoredPosition = Vector2.zero;
            g.startTime = now;

            if (Random.value < Level(seritOrani))
            {
                // Şerit: kalınlığı seviyeyle artar, boyu ekranın tamamı kadardır
                float thickness = pikselBoyutu * Random.Range(1, Mathf.Max(1, Mathf.RoundToInt(Level(seritKalinligi))) + 1);
                float speed = Level(seritHizi) * Random.Range(0.7f, 1.3f);
                c.a *= seritOpaklikCarpani;

                if (Random.value < 0.5f)
                {
                    // Yukarıdan aşağı kayan, soldan sağa boydan boya uzanan yatay şerit
                    g.kind = GlitchKind.HorizontalStrip;
                    float y = Random.Range(0.35f, 1f);
                    g.rt.anchorMin = new Vector2(0f, y);
                    g.rt.anchorMax = new Vector2(1f, y);
                    g.rt.sizeDelta = new Vector2(0f, thickness);
                    g.velocity = new Vector2(0f, -speed);
                }
                else
                {
                    // Sağdan sola kayan, yukarıdan aşağı boydan boya uzanan dikey şerit
                    g.kind = GlitchKind.VerticalStrip;
                    float x = Random.Range(0.35f, 1f);
                    g.rt.anchorMin = new Vector2(x, 0f);
                    g.rt.anchorMax = new Vector2(x, 1f);
                    g.rt.sizeDelta = new Vector2(thickness, 0f);
                    g.velocity = new Vector2(-speed, 0f);
                }

                // Şeritler kayarak yol alabilsin diye ekranda biraz daha uzun kalır
                g.life = Random.Range(0.9f, 2.2f);
            }
            else
            {
                g.kind = GlitchKind.Block;
                Vector2 anchor = new Vector2(Random.value, Random.value);
                g.rt.anchorMin = anchor;
                g.rt.anchorMax = anchor;

                // Seviye arttıkça hatalar ekranda çok daha geniş alan kaplayabilir
                int maxWidth = Mathf.Max(2, Mathf.RoundToInt(Level(pikselHataGenisligi)));
                int maxHeight = Mathf.Max(2, Mathf.RoundToInt(Level(pikselHataYuksekligi)));
                g.rt.sizeDelta = new Vector2(pikselBoyutu * Random.Range(1, maxWidth + 1), pikselBoyutu * Random.Range(1, maxHeight + 1));

                float baseLife = Level(pikselHataOmru);
                g.life = Random.Range(baseLife * 0.6f, baseLife * 1.4f);
            }

            g.color = c;
            g.img.color = new Color(c.r, c.g, c.b, 0f);
            g.active = true;
            g.img.gameObject.SetActive(true);
            return;
        }
    }

    private void UpdateShake(float now)
    {
        if (shakeTarget == null) return;

        if (!isShaking && now >= nextShakeTime)
            TriggerShake(ShakeLevel(titresimSuresi), ShakeLevel(titresimGucu));

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
