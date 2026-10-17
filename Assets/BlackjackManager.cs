using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

public enum Suit { Kupa, Karo, Maca, Sinek }

[System.Serializable]
public class Card
{
    public Suit suit;
    public int rank;      // 1 = As (Ace), 2-10 = Sayılar, 11 = J, 12 = Q, 13 = K
    public Sprite sprite; // Bu karta ait özel çizim (varsa)

    public int BaseValue
    {
        get
        {
            if (rank == 1) return 11;  // As başlangıçta 11 sayılır, toplam 21'i geçerse 1'e düşer
            if (rank >= 10) return 10; // 10, J, Q, K -> 10 puan
            return rank;
        }
    }

    public string GetRankSymbol()
    {
        return rank switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => rank.ToString()
        };
    }

    public string GetSuitSymbol()
    {
        return suit switch
        {
            Suit.Kupa => "♥",
            Suit.Karo => "♦",
            Suit.Maca => "♠",
            Suit.Sinek => "♣",
            _ => ""
        };
    }

    public bool IsRed => (suit == Suit.Kupa || suit == Suit.Karo);

    public string GetCardName() => $"{GetSuitSymbol()}{GetRankSymbol()}";
}

[System.Serializable]
public class SatilikEsya
{
    public string ad;
    public int fiyat;

    [Tooltip("Boş bırakırsan koddaki hazır pixel çizim kullanılır.")]
    public Sprite ozelCizim;

    [Tooltip("Final eşyası: satılırsa ÖLÜM sonu, 'SATMIYORUM' denirse KÜVET sonu gelir. Satmama seçeneği sadece bunda çıkar.")]
    [FormerlySerializedAs("bozulmaEfektiBaslatir")]
    public bool finalEsyasi;

    public SatilikEsya() { }

    public SatilikEsya(string ad, int fiyat, bool finalEsyasi = false)
    {
        this.ad = ad;
        this.fiyat = fiyat;
        this.finalEsyasi = finalEsyasi;
    }
}

public class BlackjackManager : MonoBehaviour
{
    [Header("Gizli Olasılık Ayarları (Sadece Inspector'dan Ayarlanır)")]
    [Tooltip("Oyuncunun kazanma yüzdesi. Oyun içinde görünmez, sadece buradan kontrol edilir.")]
    [Range(0f, 100f)]
    public float playerWinChance = 40f;

    [Tooltip("Açıkken %40/%60 hedef sonucuna uygun akıllı kart seçilir. Kapatılırsa %100 saf deste matematiği çalışır.")]
    public bool useControlledOdds = true;

    [Header("Para ve Klasik Çip Ayarları")]
    [Tooltip("Oyuncunun oyuna başlarken sahip olduğu başlangıç parası")]
    public int baslangicParasi = 100;

    [Tooltip("Sağ panelden alınan çiplerin değerleri (Klasik: 5, 10, 25, 50)")]
    public int[] cipDegerleri = new int[] { 5, 10, 25, 50 };

    [Header("Hafif Animasyon Ayarları (Kart Dönüşü & Çip Titreşimi)")]
    [Tooltip("Kartların açılırken dönme (Flip) animasyonu olsun mu?")]
    public bool kartDonmeAnimasyonu = true;

    [Tooltip("Çiplere tıklarken ve bahis koyarken hafif, düşük FPS'li hoş titreşim animasyonu olsun mu?")]
    public bool cipTitresimAnimasyonu = true;

    [Tooltip("Bilgisayarı yormaması için animasyonların saniyedeki kare sayısı (12-20 arası tatlı retro/hafif bir his verir).")]
    [Range(8, 60)]
    public int animasyonFps = 16;

    [Tooltip("Bir kartın dönerek açılma süresi (saniye)")]
    [Range(0.1f, 0.6f)]
    public float kartDonmeSuresi = 0.24f;

    [Tooltip("Çip alındığında oluşan hoş titreşimin süresi (saniye)")]
    [Range(0.08f, 0.45f)]
    public float cipTitresimSuresi = 0.18f;

    [Tooltip("Masanın kenarında sabit duran kapalı kart destesi (yığını) görünsün mü?")]
    public bool kenardaKapaliDesteGoster = true;

    [Header("Ses ve Müzik Dosyaların (Sonradan Sürükle-Bırak)")]
    [Tooltip("Arka planda sürekli döngü halinde çalacak müzik dosyası")]
    public AudioClip arkaPlanMuzigi;

    [Tooltip("Kart dağıtılırken ve dönerken çalacak ses efekti")]
    public AudioClip kartDagitmaSesi;

    [Tooltip("Sağdan çip alırken / bahis koyarken çalacak çip sesi")]
    public AudioClip cipAlmaSesi;

    [Tooltip("Butonlara tıklanırken çalacak ses efekti")]
    public AudioClip butonTiklamaSesi;

    [Tooltip("Eli kazandığında çalacak zafer sesi")]
    public AudioClip elKazanmaSesi;

    [Tooltip("Eli kaybettiğinde çalacak ses efekti")]
    public AudioClip elKaybetmeSesi;

    [Tooltip("Para tamamen bittiğinde (Game Over) çalacak ses efekti")]
    public AudioClip oyunBittiSesi;

    [Range(0f, 1f)] public float muzikSesSeviyesi = 0.5f;
    [Range(0f, 1f)] public float efektSesSeviyesi = 0.85f;
    public bool muzikAcik = true;
    public bool sesEfektleriAcik = true;

    [Header("İleride Ara Animasyon Eklemek İçin Kancalar (İsteğe Bağlı)")]
    [Tooltip("Oyun bittiğinde (para sıfırlanınca) Game Over ekranı açılmadan önceki bekleme/animasyon süresi")]
    public float oyunBittiGecisSuresi = 0.6f;
    public UnityEvent onOyunBasladi;
    public UnityEvent onElKazanildi;
    public UnityEvent onElKaybedildi;
    public UnityEvent onParaBittiGameOver;
    public UnityEvent onEsyaSatildi;

    [Header("Para Bitince Satılacak Eşyalar (Sırayla, Her Biri Tek Sefer)")]
    [Tooltip("Para sıfırlanınca açılan menüde bu eşyalar sırayla satılır. Hepsi bitince gerçek Game Over gelir.")]
    public List<SatilikEsya> satilikEsyalar = new List<SatilikEsya>
    {
        new SatilikEsya("Kol Saati", 10),
        new SatilikEsya("Cüzdan", 15),
        new SatilikEsya("Kıyafetler", 30),
        new SatilikEsya("Bacak Protezi", 40),
        new SatilikEsya("Ev Tapusu", 120),
        new SatilikEsya("Böbrek", 200, finalEsyasi: true),
    };

    [Header("İki Son (Kendi Resimlerinizi Sürükle-Bırak)")]
    [Tooltip("Böbrek satılınca açılan ölüm resmi. Boşsa örnek pixel çizim (bayılmış karakter) kullanılır.")]
    public Sprite olumSonuCizimi;
    public string olumSonuBaslik = "BÖBREĞİNİ SATTIN...";
    public string olumSonuAciklama = "Ve bir daha uyanamadın.";

    [Tooltip("Böbrek satılmayınca açılan resim. Boşsa örnek pixel çizim (buzlu küvetteki adam) kullanılır.")]
    public Sprite kuvetSonuCizimi;
    public string kuvetSonuBaslik = "BÖBREĞİNİ SATMADIN...";
    public string kuvetSonuAciklama = "Buzlu bir küvetin içinde, tek başına oturuyorsun.";

    [Tooltip("Ölüm sonu açılmadan önceki büyük sarsıntının süresi (saniye)")]
    public float olumSarsintiSuresi = 1.4f;

    [Header("Masa, Buton ve Çip Çizimlerin (İsteğe Bağlı Sürükle-Bırak)")]
    public Sprite masaArkaPlanCizimi;
    public Sprite girisEkraniArkaPlanCizimi;
    public Sprite dagitButonCizimi;
    public Sprite kartCekButonCizimi;
    public Sprite durButonCizimi;
    public Sprite ayarlarButonCizimi;
    public bool butonYazilariniGizle = false;

    [Tooltip("Klasik 4 çip için kendi çizimlerin (Sırasıyla: 5$, 10$, 25$, 50$ çip görselleri)")]
    public List<Sprite> klasikCipCizimleri = new List<Sprite>();

    [Header("Kart Çizimlerin (Sürükle-Bırak)")]
    public Sprite kartArkaYuzCizimi;
    public Sprite bosKartOnYuzCizimi;
    public Vector2 kartBoyutu = new Vector2(115f, 168f);

    [Tooltip("Kupa kart çizimlerin (Sırasıyla 13 adet: As, 2..10, J, Q, K)")]
    public List<Sprite> kupaKartlari = new List<Sprite>();

    [Tooltip("Karo kart çizimlerin (Sırasıyla 13 adet: As, 2..10, J, Q, K)")]
    public List<Sprite> karoKartlari = new List<Sprite>();

    [Tooltip("Maça kart çizimlerin (Sırasıyla 13 adet: As, 2..10, J, Q, K)")]
    public List<Sprite> macaKartlari = new List<Sprite>();

    [Tooltip("Sinek kart çizimlerin (Sırasıyla 13 adet: As, 2..10, J, Q, K)")]
    public List<Sprite> sinekKartlari = new List<Sprite>();

    [Header("Sahne ve UI Bağlantıları")]
    public bool autoCreateUI = true;

    public Image tableBackgroundImage;
    public Text moneyText;
    public Text currentBetText;
    public Text dealerScoreText;
    public Text playerScoreText;
    public Text statusText;

    public Button dealButton;
    public Button hitButton;
    public Button standButton;
    public Button clearBetButton;
    public Button settingsCornerButton;

    public Transform dealerCardArea;
    public Transform playerCardArea;
    public Transform deckPileArea;
    public Transform rightChipBarArea;
    public Transform tableBetChipPileArea;

    // Menü Panelleri
    public GameObject startMenuPanel;
    public GameObject settingsModalPanel;
    public GameObject gameOverModalPanel;
    public Text gameOverMessageText;
    public Button musicToggleButton;
    public Button sfxToggleButton;
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;

    // Ses Kaynakları
    private AudioSource musicSource;
    private AudioSource sfxSource;

    // Oyun içi listeler ve durumlar
    private List<Card> deck = new List<Card>();
    private List<Card> playerHand = new List<Card>();
    private List<Card> dealerHand = new List<Card>();

    private List<GameObject> playerCardObjects = new List<GameObject>();
    private List<GameObject> dealerCardObjects = new List<GameObject>();
    private List<GameObject> deckPileObjects = new List<GameObject>();
    private List<GameObject> chipButtonObjects = new List<GameObject>();
    private List<GameObject> betPileChipObjects = new List<GameObject>();

    private GameObject dealerHiddenCardObj;
    private Card dealerHiddenCardData;

    private int currentMoney = 100;
    private int currentBet = 0;

    private bool isRoundActive = false;
    private bool isDealingOrAnimating = false;
    private bool dealerHoleCardRevealed = false;
    private bool targetPlayerWinThisRound = false;

    // Eşya satma menüsü (her zaman çalışma anında kurulur, böylece buton bağlantıları kaybolmaz)
    private GameObject pawnShopPanel;
    private Transform pawnShopWindow;
    private Image pawnItemIcon;
    private Text pawnItemNameText;
    private Button pawnSellButton;
    private Text pawnSellButtonText;
    private Button pawnRefuseButton;
    private List<Sprite> pixelIconCache;
    private int satilanEsyaSayisi = 0;
    private ScreenDecayEffect decayEffect;

    // Son ekranı (ölüm / küvet)
    private GameObject endingPanel;
    private Image endingImage;
    private Text endingTitleText;
    private Text endingCaptionText;
    private CanvasGroup endingCanvasGroup;

    private Font defaultFont;

    private void Start()
    {
        LoadDefaultFont();
        SetupAudioSources();

        // Eğer sahnede UI henüz kurulmamışsa veya yeni eklenen para/menü panelleri eksikse tamamla
        if (autoCreateUI && (dealButton == null || moneyText == null || startMenuPanel == null))
        {
            BuildOrUpgradeCompleteUI();
        }

        EnsureLayoutSettings(playerCardArea);
        EnsureLayoutSettings(dealerCardArea);
        EnsureDeckPileVisual();
        BuildRightSideChipButtons();
        ApplyCustomTableAndButtonSprites();
        BindAllUIEvents();

        // Başlangıç parasını ayarla ve Başlangıç (Start) ekranını göster
        currentMoney = Mathf.Max(1, baslangicParasi);
        currentBet = 0;
        satilanEsyaSayisi = 0;
        UpdateMoneyAndBetUI();
        RefreshAudioSettingsUI();
        BuildPawnShopPanel();
        BuildEndingPanel();

        // Piksel hataları ve titreşimler en baştan beri var, ama çok hafif başlar
        EnsureDecayEffect();
        UpdateDecayLevel();

        ShowStartMenu();
    }

    private void BindAllUIEvents()
    {
        if (dealButton != null)
        {
            dealButton.onClick.RemoveAllListeners();
            dealButton.onClick.AddListener(() => { PlaySFX(butonTiklamaSesi); StartNewRound(); });
        }
        if (hitButton != null)
        {
            hitButton.onClick.RemoveAllListeners();
            hitButton.onClick.AddListener(() => { PlaySFX(butonTiklamaSesi); OnHitClicked(); });
        }
        if (standButton != null)
        {
            standButton.onClick.RemoveAllListeners();
            standButton.onClick.AddListener(() => { PlaySFX(butonTiklamaSesi); OnStandClicked(); });
        }
        if (clearBetButton != null)
        {
            clearBetButton.onClick.RemoveAllListeners();
            clearBetButton.onClick.AddListener(() => { PlaySFX(butonTiklamaSesi); ClearCurrentBet(); });
        }
        if (settingsCornerButton != null)
        {
            settingsCornerButton.onClick.RemoveAllListeners();
            settingsCornerButton.onClick.AddListener(() => { PlaySFX(butonTiklamaSesi); ToggleSettingsModal(); });
        }
    }

    public void SetWinChance(float newWinPercentage)
    {
        playerWinChance = Mathf.Clamp(newWinPercentage, 0f, 100f);
    }

    #region Giriş (Start) Menüsü, Ayarlar ve Oyun Bitti (Game Over) Yönetimi

    public void ShowStartMenu()
    {
        isRoundActive = false;
        isDealingOrAnimating = false;
        ClearTableCards();

        if (startMenuPanel != null) startMenuPanel.SetActive(true);
        if (settingsModalPanel != null) settingsModalPanel.SetActive(false);
        if (gameOverModalPanel != null) gameOverModalPanel.SetActive(false);

        SetButtonsInteractable(canBetAndDeal: false, canPlayHand: false);
    }

    public void OnStartGameButtonClicked()
    {
        PlaySFX(butonTiklamaSesi);
        if (startMenuPanel != null) startMenuPanel.SetActive(false);
        if (gameOverModalPanel != null) gameOverModalPanel.SetActive(false);

        if (currentMoney <= 0 && currentBet <= 0)
        {
            currentMoney = Mathf.Max(1, baslangicParasi);
        }

        ClearTableCards();
        UpdateScoreTexts();
        UpdateMoneyAndBetUI();
        SetButtonsInteractable(canBetAndDeal: true, canPlayHand: false);

        if (statusText != null)
            statusText.text = "Sağdaki çiplere tıklayarak bahis koy, sonra 'ELİ BAŞLAT'a bas!";

        onOyunBasladi?.Invoke();
    }

    public void OnRestartAfterGameOverClicked()
    {
        PlaySFX(butonTiklamaSesi);
        currentMoney = Mathf.Max(1, baslangicParasi);
        currentBet = 0;
        satilanEsyaSayisi = 0;
        UpdateDecayLevel();

        foreach (GameObject chip in betPileChipObjects)
        {
            if (chip != null) Destroy(chip);
        }
        betPileChipObjects.Clear();

        if (endingPanel != null) endingPanel.SetActive(false);
        if (pawnShopPanel != null) pawnShopPanel.SetActive(false);

        OnStartGameButtonClicked();
    }

    public void OnQuitGameButtonClicked()
    {
        PlaySFX(butonTiklamaSesi);
        Debug.Log("[Blackjack] Oyundan çıkılıyor...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void ToggleSettingsModal()
    {
        if (settingsModalPanel == null) return;
        bool nextState = !settingsModalPanel.activeSelf;
        settingsModalPanel.SetActive(nextState);
        if (nextState) settingsModalPanel.transform.SetAsLastSibling(); // Eşya satma menüsünün altında kalmasın
        RefreshAudioSettingsUI();
    }

    private IEnumerator TriggerGameOverRoutine()
    {
        isDealingOrAnimating = true;
        SetButtonsInteractable(canBetAndDeal: false, canPlayHand: false);

        if (statusText != null)
            statusText.text = "PARAN BİTTİ!";

        // İleride araya özel animasyon ekleyebilmen için geçiş süresi ve Event kancası
        onParaBittiGameOver?.Invoke();
        PlaySFX(oyunBittiSesi);

        yield return new WaitForSeconds(Mathf.Max(0.2f, oyunBittiGecisSuresi));

        isDealingOrAnimating = false;

        if (gameOverModalPanel != null)
        {
            if (gameOverMessageText != null)
                gameOverMessageText.text = $"TÜM PARAN BİTTİ VE SATACAK HİÇBİR ŞEYİN KALMADI!\n\nBaşlangıç paran olan {baslangicParasi}$ ile yeniden şansını denemek ister misin?";

            gameOverModalPanel.SetActive(true);
            // GameOver panelinde ufak bir düşük FPS açılış titreşimi
            if (cipTitresimAnimasyonu)
                StartCoroutine(WobbleTransformRoutine(gameOverModalPanel.transform, 1.05f, 3f));
        }
    }

    #endregion

    #region Para Bitince Eşya Satma Menüsü, Ekran Bozulması ve İki Son

    // 12x12 hazır pixel çizimler (satilikEsyalar sırasıyla: saat, cüzdan, kıyafet, protez, tapu, böbrek)
    private static readonly string[][] pixelEsyaCizimleri =
    {
        new[] // Kol saati
        {
            "....KKKK....",
            "....KBBK....",
            "...KKKKKK...",
            "..KYYYYYYK..",
            "..KYWWWWYK..",
            "..KYWKWWYK..",
            "..KYWKKWYK..",
            "..KYWWWWYK..",
            "..KYYYYYYK..",
            "...KKKKKK...",
            "....KBBK....",
            "....KKKK....",
        },
        new[] // Cüzdan
        {
            "............",
            "............",
            ".KKKKKKKKKK.",
            ".KBBBBBBBBK.",
            ".KBBBBBBKKKK",
            ".KBBBBBBKYYK",
            ".KBBBBBBKYYK",
            ".KBBBBBBKKKK",
            ".KBBBBBBBBK.",
            ".KKKKKKKKKK.",
            "............",
            "............",
        },
        new[] // Kıyafetler
        {
            "............",
            "...KK..KK...",
            "..KLLKKLLK..",
            ".KLLLLLLLLK.",
            "KLLLLWWLLLLK",
            "KKKLLLLLLKKK",
            "..KLLLLLLK..",
            "..KLLWLLLK..",
            "..KLLLLLLK..",
            "..KLLWLLLK..",
            "..KLLLLLLK..",
            "..KKKKKKKK..",
        },
        new[] // Bacak protezi
        {
            "....KKKK....",
            "...KGGGGK...",
            "...KGSSGK...",
            "....KGGK....",
            "....KSSK....",
            "....KSSK....",
            "....KGGK....",
            "....KSSK....",
            "....KSSK....",
            "....KSSKK...",
            "...KGGGGGK..",
            "...KKKKKKK..",
        },
        new[] // Ev tapusu
        {
            ".....KK.....",
            "....KRRK....",
            "...KRRRRK...",
            "..KRRRRRRK..",
            ".KRRRRRRRRK.",
            "KKKKKKKKKKKK",
            ".KWWWWWWWWK.",
            ".KWLLWWBBWK.",
            ".KWLLWWBBWK.",
            ".KWWWWWBBWK.",
            ".KWWWWWBBWK.",
            ".KKKKKKKKKK.",
        },
        new[] // Böbrek
        {
            "...KKKKK....",
            "..KDRRRRKK..",
            ".KDRPPRRRRK.",
            ".KDRPRRRRRK.",
            "KDRRRRKKKK..",
            "KDRRRK......",
            "KDRRRK......",
            "KDRRRRKKKK..",
            ".KDRRRRRRRK.",
            ".KDRRRRRRRK.",
            "..KDRRRRKK..",
            "...KKKKK....",
        },
    };

    // Örnek ölüm sonu: yerde yan yatmış, gözleri X olmuş bayılmış karakter
    private static readonly string[] pixelOlumCizimi =
    {
        "................................",
        "................................",
        "................................",
        "..KKKK..........................",
        ".KhhhhK.KKKKKKKKKKKKKKKKKK......",
        "KhhHHHKKLLLLLLLLLLKNNNNNNNKKK...",
        "KHKHKHHKLLLLLLLLLLKNNNNNNNNNKK..",
        "KHHKHHHKLLLLLLLLLLKNNNNNNNNNKBK.",
        "KHKHKHHKLLLLLLLLLLKNNNNNNNNNKBK.",
        ".KHHHHK.KKKKKKKKKKKKKKKKKKKKKKK.",
        "..KKKK..........................",
        "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
    };

    // Örnek küvet sonu: yandan bakış, buzlu küvette oturan adam
    private static readonly string[] pixelKuvetCizimi =
    {
        "................................",
        ".....KKKK.......................",
        "....KhhhhK......................",
        "....KhHHHHK.....................",
        "....KHHHKHK.....................",
        "....KHHHHHK.....................",
        ".....KHHHK......................",
        "...KHHHHHHHK...KcK...KccK....KcK",
        "KKKKKKKKKKKKKKKKKKKKKKKKKKKKKKKK",
        "KTCcCCcCCCcCCcCCCcCCcCCCcCCcCCTK",
        "KTCCcCCCcCCcCCCCcCCCcCCcCCCcCCTK",
        "KTcCCCcCCcCCCCcCCCcCCCCcCCcCCCTK",
        ".KTTTTTTTTTTTTTTTTTTTTTTTTTTTTK.",
        "..KTTTTTTTTTTTTTTTTTTTTTTTTTTK..",
        "...KKKKKKKKKKKKKKKKKKKKKKKKKK...",
        "...KK....................KK.....",
    };

    private static readonly Dictionary<char, Color32> pixelPaleti = new Dictionary<char, Color32>
    {
        { 'K', new Color32(22, 18, 26, 255) },    // Kontur
        { 'W', new Color32(240, 236, 224, 255) }, // Beyaz
        { 'G', new Color32(110, 116, 128, 255) }, // Koyu gri
        { 'S', new Color32(186, 194, 206, 255) }, // Gümüş
        { 'Y', new Color32(232, 186, 58, 255) },  // Altın
        { 'B', new Color32(128, 78, 40, 255) },   // Kahverengi
        { 'R', new Color32(196, 48, 52, 255) },   // Kırmızı
        { 'D', new Color32(116, 22, 34, 255) },   // Koyu kırmızı
        { 'P', new Color32(236, 128, 132, 255) }, // Pembe parlama
        { 'L', new Color32(64, 120, 200, 255) },  // Mavi
        { 'N', new Color32(40, 50, 90, 255) },    // Lacivert pantolon
        { 'H', new Color32(226, 176, 138, 255) }, // Ten
        { 'h', new Color32(70, 44, 30, 255) },    // Saç
        { 'T', new Color32(206, 210, 218, 255) }, // Küvet
        { 'C', new Color32(150, 205, 232, 255) }, // Buzlu su
        { 'c', new Color32(226, 244, 252, 255) }, // Buz kalıbı
        { 'F', new Color32(58, 54, 62, 255) },    // Zemin
    };

    private void EnsureDecayEffect()
    {
        if (decayEffect != null) return;

        RectTransform shakeTarget = tableBackgroundImage != null ? tableBackgroundImage.rectTransform : null;
        decayEffect = ScreenDecayEffect.Create(shakeTarget);
    }

    /// <summary>
    /// Her satılan eşya ekran bozulmasını (piksel hatası sıklığı, titreşim sıklığı ve şiddeti) bir tık artırır.
    /// </summary>
    private void UpdateDecayLevel()
    {
        if (decayEffect == null) return;
        int total = satilikEsyalar != null ? satilikEsyalar.Count : 0;
        decayEffect.SetLevel(satilanEsyaSayisi, total);
    }

    private void HandleOutOfMoney()
    {
        if (satilikEsyalar != null && satilanEsyaSayisi < satilikEsyalar.Count)
            StartCoroutine(OpenPawnShopRoutine());
        else
            StartCoroutine(TriggerGameOverRoutine());
    }

    private IEnumerator OpenPawnShopRoutine()
    {
        isDealingOrAnimating = true;
        SetButtonsInteractable(canBetAndDeal: false, canPlayHand: false);

        if (statusText != null)
            statusText.text = "PARAN BİTTİ... AMA HÂLÂ SATACAK BİR ŞEYİN VAR.";

        yield return new WaitForSeconds(Mathf.Max(0.2f, oyunBittiGecisSuresi));

        isDealingOrAnimating = false;

        BuildPawnShopPanel();
        if (pawnShopPanel == null)
        {
            // Canvas bulunamazsa menüsüz olarak sıradaki eşyayı sat
            OnSellCurrentItemClicked();
            yield break;
        }

        RefreshPawnShopUI();
        pawnShopPanel.transform.SetAsLastSibling();
        pawnShopPanel.SetActive(true);

        if (cipTitresimAnimasyonu && pawnShopWindow != null)
            StartCoroutine(WobbleTransformRoutine(pawnShopWindow, 1.05f, 3f));
    }

    public void OnSellCurrentItemClicked()
    {
        if (satilikEsyalar == null || satilanEsyaSayisi >= satilikEsyalar.Count) return;

        SatilikEsya esya = satilikEsyalar[satilanEsyaSayisi];
        int fiyat = Mathf.Max(1, esya.fiyat);

        satilanEsyaSayisi++;
        UpdateDecayLevel();

        PlaySFX(cipAlmaSesi);
        onEsyaSatildi?.Invoke();

        if (pawnShopPanel != null)
            pawnShopPanel.SetActive(false);

        if (esya.finalEsyasi)
        {
            StartCoroutine(PlayEndingRoutine(isDeathEnding: true));
            return;
        }

        currentMoney += fiyat;
        UpdateMoneyAndBetUI();
        SetButtonsInteractable(canBetAndDeal: true, canPlayHand: false);

        // Satış anında küçük bir sarsıntı: ekran bozulmasının arttığını hissettirir
        if (decayEffect != null)
            decayEffect.TriggerShake(0.25f, 4f + satilanEsyaSayisi);

        if (cipTitresimAnimasyonu && moneyText != null)
            StartCoroutine(WobbleTransformRoutine(moneyText.transform, 1.15f, 5f));

        if (statusText != null)
            statusText.text = $"{esya.ad} {fiyat}$'a satıldı. Masaya geri döndün, çip seçip devam et.";
    }

    public void OnRefuseFinalItemClicked()
    {
        if (satilikEsyalar == null || satilanEsyaSayisi >= satilikEsyalar.Count) return;
        if (!satilikEsyalar[satilanEsyaSayisi].finalEsyasi) return;

        PlaySFX(butonTiklamaSesi);

        if (pawnShopPanel != null)
            pawnShopPanel.SetActive(false);

        StartCoroutine(PlayEndingRoutine(isDeathEnding: false));
    }

    private IEnumerator PlayEndingRoutine(bool isDeathEnding)
    {
        isDealingOrAnimating = true;
        isRoundActive = false;
        SetButtonsInteractable(canBetAndDeal: false, canPlayHand: false);

        if (isDeathEnding)
        {
            if (statusText != null) statusText.text = "...";

            // Ölüm sonu: önce ekran sert bir şekilde sarsılır
            if (decayEffect != null)
                decayEffect.TriggerShake(olumSarsintiSuresi, 26f);

            PlaySFX(oyunBittiSesi);
            yield return new WaitForSeconds(Mathf.Max(0.2f, olumSarsintiSuresi));
        }
        else
        {
            yield return new WaitForSeconds(0.4f);
        }

        BuildEndingPanel();
        if (endingPanel != null)
        {
            Sprite customSprite = isDeathEnding ? olumSonuCizimi : kuvetSonuCizimi;
            endingImage.sprite = customSprite != null ? customSprite
                : CreatePixelSprite(isDeathEnding ? pixelOlumCizimi : pixelKuvetCizimi);

            endingTitleText.text = isDeathEnding ? olumSonuBaslik : kuvetSonuBaslik;
            endingCaptionText.text = isDeathEnding ? olumSonuAciklama : kuvetSonuAciklama;
            endingTitleText.color = isDeathEnding ? new Color(1f, 0.32f, 0.3f, 1f) : new Color(0.7f, 0.88f, 1f, 1f);

            endingPanel.transform.SetAsLastSibling();
            endingPanel.SetActive(true);

            // Düşük FPS'li, basamaklı kararak açılış
            int steps = 8;
            for (int i = 1; i <= steps; i++)
            {
                endingCanvasGroup.alpha = (float)i / steps;
                yield return new WaitForSeconds(1f / Mathf.Clamp(animasyonFps, 8, 60));
            }
            endingCanvasGroup.alpha = 1f;
        }

        isDealingOrAnimating = false;
    }

    private Transform GetRootCanvasTransform()
    {
        if (tableBackgroundImage != null && tableBackgroundImage.canvas != null)
            return tableBackgroundImage.canvas.rootCanvas.transform;

        Canvas anyCanvas = FindAnyObjectByType<Canvas>();
        return anyCanvas != null ? anyCanvas.rootCanvas.transform : null;
    }

    private void BuildPawnShopPanel()
    {
        if (pawnShopPanel != null || satilikEsyalar == null) return;

        Transform root = GetRootCanvasTransform();
        if (root == null) return;

        pawnShopPanel = CreateUIElement("PawnShopPanel", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image overlayBg = pawnShopPanel.AddComponent<Image>();
        overlayBg.color = new Color(0f, 0f, 0f, 0.86f);

        GameObject window = CreateUIElement("PawnShopWindow", pawnShopPanel.transform, new Vector2(0.22f, 0.06f), new Vector2(0.78f, 0.94f), Vector2.zero, Vector2.zero);
        pawnShopWindow = window.transform;
        Image winBg = window.AddComponent<Image>();
        winBg.color = new Color(0.15f, 0.10f, 0.08f, 0.98f);
        Outline winOutline = window.AddComponent<Outline>();
        winOutline.effectColor = new Color(0.95f, 0.75f, 0.3f, 0.9f);
        winOutline.effectDistance = new Vector2(3f, -3f);

        CreateTextElement("PawnTitle", window.transform, new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.97f),
            "PARAN BİTTİ... MASAYA DÖNMEK İÇİN SATMAN GEREKEN:", 24, TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.32f, 1f));

        // Sadece sıradaki eşya koca ekranda görünür, sonrakiler gizli kalır
        GameObject iconObj = CreateUIElement("ItemIcon", window.transform, new Vector2(0.24f, 0.36f), new Vector2(0.76f, 0.86f), Vector2.zero, Vector2.zero);
        pawnItemIcon = iconObj.AddComponent<Image>();
        pawnItemIcon.preserveAspect = true;
        pawnItemIcon.raycastTarget = false;

        pawnItemNameText = CreateTextElement("ItemName", window.transform, new Vector2(0.05f, 0.27f), new Vector2(0.95f, 0.36f),
            "", 34, TextAnchor.MiddleCenter, Color.white);

        pawnSellButton = CreateButtonElement("SellButton", window.transform, new Vector2(0.22f, 0.14f), new Vector2(0.78f, 0.25f),
            "SAT", new Color(0.20f, 0.55f, 0.28f, 1f), 24);
        pawnSellButton.onClick.AddListener(OnSellCurrentItemClicked);
        pawnSellButtonText = pawnSellButton.GetComponentInChildren<Text>();

        // Bu buton sadece final eşyasında (böbrek) görünür
        pawnRefuseButton = CreateButtonElement("RefuseButton", window.transform, new Vector2(0.22f, 0.03f), new Vector2(0.78f, 0.12f),
            "SATMIYORUM", new Color(0.55f, 0.18f, 0.20f, 1f), 20);
        pawnRefuseButton.onClick.AddListener(OnRefuseFinalItemClicked);

        pawnShopPanel.SetActive(false);
    }

    private void RefreshPawnShopUI()
    {
        if (pawnShopPanel == null || satilikEsyalar == null || satilanEsyaSayisi >= satilikEsyalar.Count) return;

        SatilikEsya esya = satilikEsyalar[satilanEsyaSayisi];

        pawnItemIcon.sprite = GetPawnIcon(satilanEsyaSayisi);
        pawnItemIcon.enabled = pawnItemIcon.sprite != null;
        pawnItemNameText.text = esya.ad;

        if (pawnSellButtonText != null)
            pawnSellButtonText.text = $"{Mathf.Max(1, esya.fiyat)}$'A SAT";

        pawnRefuseButton.gameObject.SetActive(esya.finalEsyasi);
    }

    private void BuildEndingPanel()
    {
        if (endingPanel != null) return;

        Transform root = GetRootCanvasTransform();
        if (root == null) return;

        endingPanel = CreateUIElement("EndingPanel", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image bg = endingPanel.AddComponent<Image>();
        bg.color = Color.black;
        endingCanvasGroup = endingPanel.AddComponent<CanvasGroup>();

        endingTitleText = CreateTextElement("EndingTitle", endingPanel.transform, new Vector2(0.1f, 0.84f), new Vector2(0.9f, 0.96f),
            "", 40, TextAnchor.MiddleCenter, Color.white);

        GameObject imageObj = CreateUIElement("EndingImage", endingPanel.transform, new Vector2(0.12f, 0.26f), new Vector2(0.88f, 0.83f), Vector2.zero, Vector2.zero);
        endingImage = imageObj.AddComponent<Image>();
        endingImage.preserveAspect = true;
        endingImage.raycastTarget = false;

        endingCaptionText = CreateTextElement("EndingCaption", endingPanel.transform, new Vector2(0.1f, 0.16f), new Vector2(0.9f, 0.25f),
            "", 24, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.85f, 1f));

        Button retryBtn = CreateButtonElement("EndingRetryButton", endingPanel.transform, new Vector2(0.28f, 0.04f), new Vector2(0.49f, 0.13f),
            "BAŞTAN BAŞLA", new Color(0.18f, 0.55f, 0.30f, 1f), 20);
        retryBtn.onClick.AddListener(OnRestartAfterGameOverClicked);

        Button quitBtn = CreateButtonElement("EndingQuitButton", endingPanel.transform, new Vector2(0.51f, 0.04f), new Vector2(0.72f, 0.13f),
            "ÇIKIŞ", new Color(0.62f, 0.20f, 0.20f, 1f), 20);
        quitBtn.onClick.AddListener(OnQuitGameButtonClicked);

        endingPanel.SetActive(false);
    }

    private Sprite GetPawnIcon(int index)
    {
        if (satilikEsyalar[index].ozelCizim != null)
            return satilikEsyalar[index].ozelCizim;

        if (pixelIconCache == null)
        {
            pixelIconCache = new List<Sprite>();
            foreach (string[] rows in pixelEsyaCizimleri)
                pixelIconCache.Add(CreatePixelSprite(rows));
        }

        return index < pixelIconCache.Count ? pixelIconCache[index] : null;
    }

    private static Sprite CreatePixelSprite(string[] rows)
    {
        int h = rows.Length;
        int w = 0;
        foreach (string row in rows) w = Mathf.Max(w, row.Length);

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point; // Büyütünce bulanıklaşmaz, keskin pixel görünür
        tex.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                char c = x < rows[y].Length ? rows[y][x] : '.';
                // Texture'da y=0 en alt satırdır, çizimi ters çeviriyoruz
                pixels[(h - 1 - y) * w + x] = pixelPaleti.TryGetValue(c, out Color32 col) ? col : new Color32(0, 0, 0, 0);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    #endregion

    #region Sol Üst Para ve Sağdan Çip Alma Sistemi

    /// <summary>
    /// Sağ taraftaki klasik çiplerden birine tıklandığında çalışır.
    /// </summary>
    public void OnChipClicked(int chipValue, Transform clickedChipTransform, Color chipColor, Sprite chipSprite)
    {
        if (isRoundActive || isDealingOrAnimating) return;

        // Çipe tıklandığında her durumda ufak bir titreşim ver
        if (cipTitresimAnimasyonu && clickedChipTransform != null)
        {
            StartCoroutine(WobbleTransformRoutine(clickedChipTransform, 1.14f, 9f));
        }

        if (currentMoney < chipValue)
        {
            if (statusText != null)
                statusText.text = $"Yetersiz bakiye! ({chipValue}$ çip için paran yetmiyor)";
            return;
        }

        currentMoney -= chipValue;
        currentBet += chipValue;

        PlaySFX(cipAlmaSesi);
        SpawnChipInBetPile(chipValue, chipColor, chipSprite);
        UpdateMoneyAndBetUI();
        SetButtonsInteractable(canBetAndDeal: true, canPlayHand: false);

        if (statusText != null)
            statusText.text = $"Ortadaki Bahis: {currentBet}$ — Hazırsan 'ELİ BAŞLAT'a bas!";
    }

    public void ClearCurrentBet()
    {
        if (isRoundActive || isDealingOrAnimating || currentBet <= 0) return;

        currentMoney += currentBet;
        currentBet = 0;

        foreach (GameObject chip in betPileChipObjects)
        {
            if (chip != null) Destroy(chip);
        }
        betPileChipObjects.Clear();

        UpdateMoneyAndBetUI();
        SetButtonsInteractable(canBetAndDeal: true, canPlayHand: false);

        if (statusText != null)
            statusText.text = "Bahis geri alındı. Sağdan çip seçerek bahis koyabilirsin.";
    }

    private void SpawnChipInBetPile(int chipValue, Color chipColor, Sprite chipSprite)
    {
        if (tableBetChipPileArea == null) return;

        GameObject chipToken = CreateClassicChipVisual($"BetChip_{chipValue}", tableBetChipPileArea, chipValue, chipColor, chipSprite, 68f);
        RectTransform rt = chipToken.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);

        int stackIndex = betPileChipObjects.Count;
        float offsetX = UnityEngine.Random.Range(-8f, 8f);
        float offsetY = Mathf.Min(stackIndex * 5f, 45f);
        rt.anchoredPosition = new Vector2(offsetX, offsetY);

        betPileChipObjects.Add(chipToken);

        if (cipTitresimAnimasyonu)
        {
            StartCoroutine(WobbleTransformRoutine(chipToken.transform, 1.18f, 11f));
        }
    }

    /// <summary>
    /// Bilgisayarı hiç yormayan, düşük FPS'li hoş çip titreşim (Wobble) animasyonu.
    /// </summary>
    private IEnumerator WobbleTransformRoutine(Transform target, float maxScale, float maxAngleDeg)
    {
        if (target == null) yield break;

        int fps = Mathf.Clamp(animasyonFps, 8, 60);
        float stepTime = 1f / fps;
        int totalSteps = Mathf.Max(3, Mathf.RoundToInt(cipTitresimSuresi / stepTime));

        float[] angles = new float[] { maxAngleDeg, -maxAngleDeg * 0.8f, maxAngleDeg * 0.5f, -maxAngleDeg * 0.25f, 0f };

        for (int i = 0; i < totalSteps; i++)
        {
            if (target == null) yield break;
            float progress = (float)i / totalSteps;
            float scale = Mathf.Lerp(maxScale, 1f, progress);
            float angle = angles[i % angles.Length] * (1f - progress);

            target.localScale = new Vector3(scale, scale, 1f);
            target.localRotation = Quaternion.Euler(0f, 0f, angle);

            yield return new WaitForSeconds(stepTime);
        }

        if (target != null)
        {
            target.localScale = Vector3.one;
            target.localRotation = Quaternion.identity;
        }
    }

    private void UpdateMoneyAndBetUI()
    {
        if (moneyText != null)
        {
            moneyText.text = $"PARA: {currentMoney}$";
        }
        if (currentBetText != null)
        {
            currentBetText.text = $"BAHİS: {currentBet}$";
        }
    }

    #endregion

    #region Oyun Akışı (Sıralı Dağıtım, Hit, Stand, Ödül Hesaplama)

    public void StartNewRound()
    {
        if (isDealingOrAnimating || isRoundActive) return;

        // Eğer oyuncu henüz çip seçmeden Eli Başlat'a bastıysa, parası yetiyorsa en küçük çipi otomatik koy veya uyar
        if (currentBet <= 0)
        {
            int minChip = (cipDegerleri != null && cipDegerleri.Length > 0) ? cipDegerleri[0] : 5;
            if (currentMoney >= minChip)
            {
                Color defaultRed = new Color(0.82f, 0.16f, 0.18f, 1f);
                Sprite customSp = (klasikCipCizimleri != null && klasikCipCizimleri.Count > 0) ? klasikCipCizimleri[0] : null;
                OnChipClicked(minChip, null, defaultRed, customSp);
            }
            else if (currentMoney > 0)
            {
                // Parası en küçük çipten az kaldıysa kalan tüm parasını (All-in) bahse koy
                int allIn = currentMoney;
                currentMoney = 0;
                currentBet = allIn;
                SpawnChipInBetPile(allIn, new Color(0.85f, 0.65f, 0.12f, 1f), null);
                UpdateMoneyAndBetUI();
            }
            else
            {
                HandleOutOfMoney();
                return;
            }
        }

        StopAllCoroutines();
        ClearTableCards();
        BuildAndShuffleDeck();
        EnsureDeckPileVisual();

        isRoundActive = true;
        dealerHoleCardRevealed = false;

        // Inspector'daki gizli yüzdelik orana göre (%40 Kazanma / %60 Kaybetme) bu elin hedefini seç
        float roll = UnityEngine.Random.Range(0f, 100f);
        targetPlayerWinThisRound = (roll < playerWinChance);

        StartCoroutine(InitialDealRoutine());
    }

    private IEnumerator InitialDealRoutine()
    {
        isDealingOrAnimating = true;
        SetButtonsInteractable(canBetAndDeal: false, canPlayHand: false);

        if (statusText != null)
            statusText.text = "Kartlar dağıtılıyor...";

        UpdateScoreTexts();

        // 1. Oyuncunun 1. kartı
        Card pCard1 = DrawCardForInitialDeal(forPlayer: true);
        playerHand.Add(pCard1);
        PlaySFX(kartDagitmaSesi);
        yield return StartCoroutine(SpawnAndFlipCardRoutine(pCard1, playerCardArea, playerCardObjects, stayHidden: false));
        UpdateScoreTexts();

        // 2. Kurpiyerin 1. kartı (Açık)
        Card dCard1 = DrawCardForInitialDeal(forPlayer: false);
        dealerHand.Add(dCard1);
        PlaySFX(kartDagitmaSesi);
        yield return StartCoroutine(SpawnAndFlipCardRoutine(dCard1, dealerCardArea, dealerCardObjects, stayHidden: false));
        UpdateScoreTexts();

        // 3. Oyuncunun 2. kartı
        Card pCard2 = DrawCardForInitialDeal(forPlayer: true);
        playerHand.Add(pCard2);
        PlaySFX(kartDagitmaSesi);
        yield return StartCoroutine(SpawnAndFlipCardRoutine(pCard2, playerCardArea, playerCardObjects, stayHidden: false));
        UpdateScoreTexts();

        // 4. Kurpiyerin 2. kartı (Kapalı durur, sıra kurpiyere gelince dönerek açılır)
        Card dCard2 = DrawCardForInitialDeal(forPlayer: false);
        dealerHand.Add(dCard2);
        dealerHiddenCardData = dCard2;
        PlaySFX(kartDagitmaSesi);
        yield return StartCoroutine(SpawnAndFlipCardRoutine(dCard2, dealerCardArea, dealerCardObjects, stayHidden: true));
        dealerHiddenCardObj = dealerCardObjects[dealerCardObjects.Count - 1];
        UpdateScoreTexts();

        isDealingOrAnimating = false;

        int playerScore = CalculateHandValue(playerHand);
        if (playerScore == 21)
        {
            OnStandClicked();
            yield break;
        }

        if (statusText != null)
            statusText.text = "Sıra sende: KART ÇEK veya DUR";

        SetButtonsInteractable(canBetAndDeal: false, canPlayHand: true);
    }

    public void OnHitClicked()
    {
        if (!isRoundActive || isDealingOrAnimating) return;

        StartCoroutine(PlayerHitRoutine());
    }

    private IEnumerator PlayerHitRoutine()
    {
        isDealingOrAnimating = true;
        SetButtonsInteractable(canBetAndDeal: false, canPlayHand: false);

        Card drawnCard = DrawSmartCardForPlayer();
        playerHand.Add(drawnCard);

        PlaySFX(kartDagitmaSesi);
        yield return StartCoroutine(SpawnAndFlipCardRoutine(drawnCard, playerCardArea, playerCardObjects, stayHidden: false));
        UpdateScoreTexts();

        int playerScore = CalculateHandValue(playerHand);

        if (playerScore > 21)
        {
            if (!dealerHoleCardRevealed && dealerHiddenCardObj != null)
            {
                dealerHoleCardRevealed = true;
                PlaySFX(kartDagitmaSesi);
                yield return StartCoroutine(FlipExistingCardOpenRoutine(dealerHiddenCardObj, dealerHiddenCardData));
                UpdateScoreTexts();
            }

            isDealingOrAnimating = false;
            FinishRoundWithPayout(playerWon: false, isPush: false, $"21'i Geçtin ({playerScore})! {currentBet}$ Kaybettin.");
        }
        else if (playerScore == 21)
        {
            isDealingOrAnimating = false;
            OnStandClicked();
        }
        else
        {
            isDealingOrAnimating = false;
            SetButtonsInteractable(canBetAndDeal: false, canPlayHand: true);
        }
    }

    public void OnStandClicked()
    {
        if (!isRoundActive || isDealingOrAnimating) return;

        SetButtonsInteractable(canBetAndDeal: false, canPlayHand: false);
        StartCoroutine(DealerTurnRoutine());
    }

    private IEnumerator DealerTurnRoutine()
    {
        isDealingOrAnimating = true;

        if (statusText != null)
            statusText.text = "Kurpiyer kapalı kartını açıyor...";

        if (!dealerHoleCardRevealed)
        {
            dealerHoleCardRevealed = true;
            if (dealerHiddenCardObj != null)
            {
                PlaySFX(kartDagitmaSesi);
                yield return StartCoroutine(FlipExistingCardOpenRoutine(dealerHiddenCardObj, dealerHiddenCardData));
            }
            UpdateScoreTexts();
        }

        yield return new WaitForSeconds(0.35f);

        while (CalculateHandValue(dealerHand) < 17)
        {
            if (statusText != null)
                statusText.text = "Kurpiyer kart çekiyor...";

            Card dealerCard = DrawSmartCardForDealer();
            dealerHand.Add(dealerCard);

            PlaySFX(kartDagitmaSesi);
            yield return StartCoroutine(SpawnAndFlipCardRoutine(dealerCard, dealerCardArea, dealerCardObjects, stayHidden: false));
            UpdateScoreTexts();

            yield return new WaitForSeconds(0.35f);
        }

        isDealingOrAnimating = false;
        EvaluateWinner();
    }

    private void EvaluateWinner()
    {
        int playerScore = CalculateHandValue(playerHand);
        int dealerScore = CalculateHandValue(dealerHand);

        if (dealerScore > 21)
        {
            FinishRoundWithPayout(playerWon: true, isPush: false, $"Kurpiyer 21'i Geçti ({dealerScore})! +{currentBet}$ KAZANDIN!");
        }
        else if (playerScore > dealerScore)
        {
            FinishRoundWithPayout(playerWon: true, isPush: false, $"KAZANDIN! ({playerScore} - {dealerScore}) | +{currentBet}$");
        }
        else if (playerScore < dealerScore)
        {
            FinishRoundWithPayout(playerWon: false, isPush: false, $"KASA KAZANDI! ({dealerScore} - {playerScore}) | -{currentBet}$");
        }
        else
        {
            FinishRoundWithPayout(playerWon: false, isPush: true, $"BERABERE! ({playerScore} - {dealerScore}) | Bahsin İade Edildi.");
        }
    }

    private void FinishRoundWithPayout(bool playerWon, bool isPush, string reason)
    {
        isRoundActive = false;
        isDealingOrAnimating = false;

        if (playerWon)
        {
            currentMoney += currentBet * 2;
            PlaySFX(elKazanmaSesi);
            onElKazanildi?.Invoke();
            if (cipTitresimAnimasyonu && moneyText != null)
                StartCoroutine(WobbleTransformRoutine(moneyText.transform, 1.15f, 5f));
        }
        else if (isPush)
        {
            currentMoney += currentBet;
        }
        else
        {
            PlaySFX(elKaybetmeSesi);
            onElKaybedildi?.Invoke();
        }

        currentBet = 0;
        foreach (GameObject chip in betPileChipObjects)
        {
            if (chip != null) Destroy(chip);
        }
        betPileChipObjects.Clear();

        UpdateMoneyAndBetUI();

        if (statusText != null)
            statusText.text = reason;

        // Para bittiyse önce eşya satma menüsü açılır, satacak bir şey kalmadıysa Game Over
        if (currentMoney <= 0)
        {
            HandleOutOfMoney();
        }
        else
        {
            SetButtonsInteractable(canBetAndDeal: true, canPlayHand: false);
        }
    }

    #endregion

    #region Ses ve Müzik Sistemi (Inspector'dan Kolay Ekleme)

    private void SetupAudioSources()
    {
        AudioSource[] sources = GetComponents<AudioSource>();
        if (sources.Length >= 2)
        {
            musicSource = sources[0];
            sfxSource = sources[1];
        }
        else
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            sfxSource = gameObject.AddComponent<AudioSource>();
        }

        musicSource.loop = true;
        musicSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;

        ApplyAudioSettings();

        if (arkaPlanMuzigi != null && muzikAcik)
        {
            musicSource.clip = arkaPlanMuzigi;
            musicSource.Play();
        }
    }

    public void ToggleMusic()
    {
        muzikAcik = !muzikAcik;
        ApplyAudioSettings();
        PlaySFX(butonTiklamaSesi);
        RefreshAudioSettingsUI();
    }

    public void ToggleSFX()
    {
        sesEfektleriAcik = !sesEfektleriAcik;
        ApplyAudioSettings();
        PlaySFX(butonTiklamaSesi);
        RefreshAudioSettingsUI();
    }

    public void OnMusicVolumeChanged(float val)
    {
        muzikSesSeviyesi = Mathf.Clamp01(val);
        ApplyAudioSettings();
    }

    public void OnSFXVolumeChanged(float val)
    {
        efektSesSeviyesi = Mathf.Clamp01(val);
        ApplyAudioSettings();
    }

    private void ApplyAudioSettings()
    {
        if (musicSource != null)
        {
            musicSource.mute = !muzikAcik;
            musicSource.volume = muzikSesSeviyesi;
            if (arkaPlanMuzigi != null && musicSource.clip != arkaPlanMuzigi)
            {
                musicSource.clip = arkaPlanMuzigi;
            }
            if (muzikAcik && arkaPlanMuzigi != null && !musicSource.isPlaying)
            {
                musicSource.Play();
            }
        }

        if (sfxSource != null)
        {
            sfxSource.mute = !sesEfektleriAcik;
            sfxSource.volume = efektSesSeviyesi;
        }
    }

    private void PlaySFX(AudioClip clip)
    {
        if (!sesEfektleriAcik || clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, efektSesSeviyesi);
    }

    private void RefreshAudioSettingsUI()
    {
        if (musicToggleButton != null)
        {
            Text t = musicToggleButton.GetComponentInChildren<Text>();
            Image img = musicToggleButton.GetComponent<Image>();
            if (t != null) t.text = muzikAcik ? "MÜZİK: AÇIK" : "MÜZİK: KAPALI";
            if (img != null) img.color = muzikAcik ? new Color(0.18f, 0.62f, 0.32f, 1f) : new Color(0.65f, 0.22f, 0.22f, 1f);
        }

        if (sfxToggleButton != null)
        {
            Text t = sfxToggleButton.GetComponentInChildren<Text>();
            Image img = sfxToggleButton.GetComponent<Image>();
            if (t != null) t.text = sesEfektleriAcik ? "SES EFEKTLERİ: AÇIK" : "SES EFEKTLERİ: KAPALI";
            if (img != null) img.color = sesEfektleriAcik ? new Color(0.18f, 0.62f, 0.32f, 1f) : new Color(0.65f, 0.22f, 0.22f, 1f);
        }

        if (musicVolumeSlider != null)
            musicVolumeSlider.SetValueWithoutNotify(muzikSesSeviyesi);

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.SetValueWithoutNotify(efektSesSeviyesi);
    }

    #endregion

    #region Hafif (Düşük FPS) Kart Dönme Animasyonu ve Görsel Oluşturma

    private IEnumerator SpawnAndFlipCardRoutine(Card card, Transform parentArea, List<GameObject> targetList, bool stayHidden)
    {
        if (parentArea == null) yield break;

        bool createInitiallyHidden = stayHidden || kartDonmeAnimasyonu;
        GameObject cardObj = CreateCardGameObject(card, parentArea, isHidden: createInitiallyHidden);
        targetList.Add(cardObj);

        if (stayHidden || !kartDonmeAnimasyonu)
        {
            yield return new WaitForSeconds(0.08f);
            yield break;
        }

        yield return StartCoroutine(FlipExistingCardOpenRoutine(cardObj, card));
    }

    private IEnumerator FlipExistingCardOpenRoutine(GameObject cardObj, Card card)
    {
        if (cardObj == null) yield break;

        RectTransform rt = cardObj.GetComponent<RectTransform>();
        if (!kartDonmeAnimasyonu || rt == null)
        {
            ApplyCardVisualFace(cardObj, card, isHidden: false);
            yield break;
        }

        int fps = Mathf.Clamp(animasyonFps, 8, 60);
        float stepTime = 1f / fps;
        float halfDuration = Mathf.Max(0.05f, kartDonmeSuresi * 0.5f);
        int stepsPerHalf = Mathf.Max(2, Mathf.RoundToInt(halfDuration / stepTime));

        for (int i = 1; i <= stepsPerHalf; i++)
        {
            if (rt == null) yield break;
            float t = (float)i / stepsPerHalf;
            rt.localScale = new Vector3(Mathf.Lerp(1f, 0f, t), 1f, 1f);
            yield return new WaitForSeconds(stepTime);
        }

        if (cardObj != null)
        {
            ApplyCardVisualFace(cardObj, card, isHidden: false);
        }

        for (int i = 1; i <= stepsPerHalf; i++)
        {
            if (rt == null) yield break;
            float t = (float)i / stepsPerHalf;
            rt.localScale = new Vector3(Mathf.Lerp(0f, 1f, t), 1f, 1f);
            yield return new WaitForSeconds(stepTime);
        }

        if (rt != null)
            rt.localScale = Vector3.one;
    }

    private GameObject CreateCardGameObject(Card card, Transform parentArea, bool isHidden)
    {
        GameObject cardObj = new GameObject(isHidden ? "ClosedCard" : card.GetCardName(), typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        cardObj.transform.SetParent(parentArea, false);

        RectTransform rt = cardObj.GetComponent<RectTransform>();
        rt.sizeDelta = kartBoyutu;
        rt.localScale = Vector3.one;

        LayoutElement le = cardObj.GetComponent<LayoutElement>();
        le.preferredWidth = kartBoyutu.x;
        le.preferredHeight = kartBoyutu.y;
        le.minWidth = kartBoyutu.x * 0.65f;
        le.minHeight = kartBoyutu.y;

        Outline outline = cardObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.45f);
        outline.effectDistance = new Vector2(2f, -2f);

        GameObject labelObj = new GameObject("CardLabel", typeof(RectTransform), typeof(Text));
        labelObj.transform.SetParent(cardObj.transform, false);

        RectTransform labelRt = labelObj.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        Text txt = labelObj.GetComponent<Text>();
        txt.font = defaultFont;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.fontStyle = FontStyle.Bold;

        ApplyCardVisualFace(cardObj, card, isHidden);
        return cardObj;
    }

    private void ApplyCardVisualFace(GameObject cardObj, Card card, bool isHidden)
    {
        if (cardObj == null) return;

        Image bgImage = cardObj.GetComponent<Image>();
        Outline outline = cardObj.GetComponent<Outline>();
        Text txt = cardObj.GetComponentInChildren<Text>(true);

        bgImage.preserveAspect = true;
        cardObj.name = isHidden ? "ClosedCard" : card.GetCardName();

        if (isHidden)
        {
            if (kartArkaYuzCizimi != null)
            {
                bgImage.sprite = kartArkaYuzCizimi;
                bgImage.color = Color.white;
                if (outline != null) outline.enabled = false;
                if (txt != null) txt.text = "";
            }
            else
            {
                bgImage.sprite = null;
                bgImage.color = new Color(0.16f, 0.25f, 0.45f, 1f);
                if (outline != null) outline.enabled = true;
                if (txt != null)
                {
                    txt.text = "♦\nKAPALI";
                    txt.fontSize = 20;
                    txt.color = new Color(0.85f, 0.9f, 1f, 1f);
                }
            }
            return;
        }

        if (card.sprite != null)
        {
            bgImage.sprite = card.sprite;
            bgImage.color = Color.white;
            if (outline != null) outline.enabled = false;
            if (txt != null) txt.text = "";
            return;
        }

        if (bosKartOnYuzCizimi != null)
        {
            bgImage.sprite = bosKartOnYuzCizimi;
            bgImage.color = Color.white;
            if (outline != null) outline.enabled = false;
        }
        else
        {
            bgImage.sprite = null;
            bgImage.color = new Color(0.98f, 0.98f, 0.96f, 1f);
            if (outline != null) outline.enabled = true;
        }

        if (txt != null)
        {
            txt.text = $"{card.GetRankSymbol()}\n{card.GetSuitSymbol()}";
            txt.fontSize = 36;
            txt.color = card.IsRed ? new Color(0.82f, 0.12f, 0.15f, 1f) : new Color(0.12f, 0.14f, 0.18f, 1f);
        }
    }

    private void EnsureDeckPileVisual()
    {
        if (!kenardaKapaliDesteGoster)
        {
            if (deckPileArea != null) deckPileArea.gameObject.SetActive(false);
            return;
        }

        if (deckPileArea == null && tableBackgroundImage != null)
        {
            GameObject deckAreaObj = CreateUIElement("DeckPileArea", tableBackgroundImage.transform,
                new Vector2(0.75f, 0.56f), new Vector2(0.86f, 0.82f), Vector2.zero, Vector2.zero);
            deckPileArea = deckAreaObj.transform;
        }

        if (deckPileArea == null) return;
        deckPileArea.gameObject.SetActive(true);

        foreach (GameObject obj in deckPileObjects)
        {
            if (obj != null) Destroy(obj);
        }
        deckPileObjects.Clear();

        Card dummyCard = new Card { suit = Suit.Maca, rank = 1 };
        for (int i = 0; i < 3; i++)
        {
            GameObject stackCard = CreateCardGameObject(dummyCard, deckPileArea, isHidden: true);
            stackCard.name = $"DeckCard_{i + 1}";
            RectTransform rt = stackCard.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-i * 6f, i * 6f);
            deckPileObjects.Add(stackCard);
        }
    }

    private void BuildRightSideChipButtons()
    {
        if (rightChipBarArea == null) return;

        foreach (GameObject obj in chipButtonObjects)
        {
            if (obj != null) Destroy(obj);
        }
        chipButtonObjects.Clear();

        if (cipDegerleri == null || cipDegerleri.Length == 0)
            cipDegerleri = new int[] { 5, 10, 25, 50 };

        Color[] classicColors = new Color[]
        {
            new Color(0.82f, 0.16f, 0.18f, 1f), // 5$ Klasik Kırmızı
            new Color(0.16f, 0.42f, 0.78f, 1f), // 10$ Klasik Mavi
            new Color(0.16f, 0.62f, 0.30f, 1f), // 25$ Klasik Yeşil
            new Color(0.18f, 0.18f, 0.22f, 1f)  // 50$ Klasik Siyah/Altın
        };

        for (int i = 0; i < cipDegerleri.Length; i++)
        {
            int val = cipDegerleri[i];
            Color cColor = classicColors[i % classicColors.Length];
            Sprite customChipSprite = (klasikCipCizimleri != null && i < klasikCipCizimleri.Count) ? klasikCipCizimleri[i] : null;

            GameObject chipBtnObj = CreateClassicChipVisual($"ChipBtn_{val}", rightChipBarArea, val, cColor, customChipSprite, 86f);
            Button btn = chipBtnObj.AddComponent<Button>();
            btn.targetGraphic = chipBtnObj.GetComponent<Image>();

            Transform chipTransform = chipBtnObj.transform;
            btn.onClick.AddListener(() => OnChipClicked(val, chipTransform, cColor, customChipSprite));

            chipButtonObjects.Add(chipBtnObj);
        }
    }

    private GameObject CreateClassicChipVisual(string objName, Transform parent, int chipValue, Color chipColor, Sprite customSprite, float size)
    {
        GameObject chipObj = new GameObject(objName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        chipObj.transform.SetParent(parent, false);

        RectTransform rt = chipObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);

        LayoutElement le = chipObj.GetComponent<LayoutElement>();
        le.preferredWidth = size;
        le.preferredHeight = size;

        Image outerImg = chipObj.GetComponent<Image>();
        outerImg.preserveAspect = true;

        if (customSprite != null)
        {
            outerImg.sprite = customSprite;
            outerImg.color = Color.white;
            if (!butonYazilariniGizle)
            {
                CreateTextElement("ChipLabel", chipObj.transform, Vector2.zero, Vector2.one, $"{chipValue}$", Mathf.RoundToInt(size * 0.28f), TextAnchor.MiddleCenter, Color.white);
            }
            return chipObj;
        }

        // Özel çizim atanmadıysa şık klasik casino çipi görünümü oluştur (Dış halka + İç göbek + Değer)
        outerImg.color = chipColor;

        Outline outerBorder = chipObj.AddComponent<Outline>();
        outerBorder.effectColor = new Color(0.95f, 0.92f, 0.82f, 0.95f); // Beyaz/Krem klasik çip şeridi
        outerBorder.effectDistance = new Vector2(3f, -3f);

        GameObject innerRing = CreateUIElement("InnerRing", chipObj.transform, new Vector2(0.14f, 0.14f), new Vector2(0.86f, 0.86f), Vector2.zero, Vector2.zero);
        Image innerImg = innerRing.AddComponent<Image>();
        innerImg.color = new Color(chipColor.r * 0.82f, chipColor.g * 0.82f, chipColor.b * 0.82f, 1f);

        Outline innerBorder = innerRing.AddComponent<Outline>();
        innerBorder.effectColor = new Color(1f, 0.88f, 0.45f, 0.85f);
        innerBorder.effectDistance = new Vector2(1.5f, -1.5f);

        CreateTextElement("ChipValueText", innerRing.transform, Vector2.zero, Vector2.one, $"{chipValue}$", Mathf.RoundToInt(size * 0.28f), TextAnchor.MiddleCenter, Color.white);

        return chipObj;
    }

    #endregion

    #region Akıllı Olasılık ve Kart Seçim Algoritması (%40 / %60 Kontrolü)

    private Card DrawCardForInitialDeal(bool forPlayer)
    {
        if (!useControlledOdds || deck.Count == 0)
            return DrawTopCard();

        if (forPlayer && !targetPlayerWinThisRound && playerHand.Count == 1)
        {
            int index = deck.FindIndex(c => CalculateSimulatedValue(playerHand, c) < 21);
            if (index != -1) return PullCardAt(index);
        }

        if (!forPlayer && targetPlayerWinThisRound && dealerHand.Count == 1)
        {
            int index = deck.FindIndex(c => CalculateSimulatedValue(dealerHand, c) < 21);
            if (index != -1) return PullCardAt(index);
        }

        return DrawTopCard();
    }

    private Card DrawSmartCardForPlayer()
    {
        if (!useControlledOdds || deck.Count == 0)
            return DrawTopCard();

        int currentScore = CalculateHandValue(playerHand);

        if (targetPlayerWinThisRound)
        {
            int sweetSpotIndex = deck.FindIndex(c =>
            {
                int nextVal = CalculateSimulatedValue(playerHand, c);
                return nextVal >= 18 && nextVal <= 21;
            });
            if (sweetSpotIndex != -1) return PullCardAt(sweetSpotIndex);

            int safeIndex = deck.FindIndex(c => CalculateSimulatedValue(playerHand, c) <= 21);
            if (safeIndex != -1) return PullCardAt(safeIndex);
        }
        else
        {
            if (currentScore >= 13)
            {
                int bustIndex = deck.FindIndex(c => CalculateSimulatedValue(playerHand, c) > 21);
                if (bustIndex != -1 && UnityEngine.Random.value < 0.70f)
                    return PullCardAt(bustIndex);
            }
        }

        return DrawTopCard();
    }

    private Card DrawSmartCardForDealer()
    {
        if (!useControlledOdds || deck.Count == 0)
            return DrawTopCard();

        int playerScore = CalculateHandValue(playerHand);

        if (targetPlayerWinThisRound)
        {
            if (playerScore >= 18)
            {
                int losingStandIndex = deck.FindIndex(c =>
                {
                    int nextVal = CalculateSimulatedValue(dealerHand, c);
                    return nextVal >= 17 && nextVal < playerScore;
                });
                if (losingStandIndex != -1) return PullCardAt(losingStandIndex);
            }

            int dealerBustIndex = deck.FindIndex(c => CalculateSimulatedValue(dealerHand, c) > 21);
            if (dealerBustIndex != -1) return PullCardAt(dealerBustIndex);

            int stepIndex = deck.FindIndex(c =>
            {
                int nextVal = CalculateSimulatedValue(dealerHand, c);
                return nextVal >= 13 && nextVal <= 16;
            });
            if (stepIndex != -1) return PullCardAt(stepIndex);
        }
        else
        {
            int minWinningScore = Mathf.Max(17, playerScore + 1);

            if (minWinningScore <= 21)
            {
                int winningIndex = deck.FindIndex(c =>
                {
                    int nextVal = CalculateSimulatedValue(dealerHand, c);
                    return nextVal >= minWinningScore && nextVal <= 21;
                });
                if (winningIndex != -1) return PullCardAt(winningIndex);
            }

            int bestStepIndex = -1;
            int bestStepVal = -1;
            for (int i = 0; i < deck.Count; i++)
            {
                int nextVal = CalculateSimulatedValue(dealerHand, deck[i]);
                if (nextVal <= 16 && nextVal > bestStepVal)
                {
                    bestStepVal = nextVal;
                    bestStepIndex = i;
                }
            }
            if (bestStepIndex != -1) return PullCardAt(bestStepIndex);
        }

        return DrawTopCard();
    }

    private int CalculateSimulatedValue(List<Card> currentHand, Card candidateCard)
    {
        List<Card> temp = new List<Card>(currentHand) { candidateCard };
        return CalculateHandValue(temp);
    }

    #endregion

    #region Deste ve Puan Hesaplama

    private void BuildAndShuffleDeck()
    {
        deck.Clear();

        foreach (Suit suit in Enum.GetValues(typeof(Suit)))
        {
            for (int rank = 1; rank <= 13; rank++)
            {
                deck.Add(new Card
                {
                    suit = suit,
                    rank = rank,
                    sprite = GetCustomCardSprite(suit, rank)
                });
            }
        }

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int rnd = UnityEngine.Random.Range(0, i + 1);
            Card temp = deck[i];
            deck[i] = deck[rnd];
            deck[rnd] = temp;
        }
    }

    private Sprite GetCustomCardSprite(Suit suit, int rank)
    {
        int index = rank - 1;
        List<Sprite> targetList = suit switch
        {
            Suit.Kupa => kupaKartlari,
            Suit.Karo => karoKartlari,
            Suit.Maca => macaKartlari,
            Suit.Sinek => sinekKartlari,
            _ => null
        };

        if (targetList != null && index >= 0 && index < targetList.Count && targetList[index] != null)
        {
            return targetList[index];
        }

        return null;
    }

    private Card DrawTopCard() => PullCardAt(0);

    private Card PullCardAt(int index)
    {
        Card selected = deck[index];
        deck.RemoveAt(index);
        return selected;
    }

    public int CalculateHandValue(List<Card> hand)
    {
        int total = 0;
        int aceCount = 0;

        foreach (Card card in hand)
        {
            total += card.BaseValue;
            if (card.rank == 1) aceCount++;
        }

        while (total > 21 && aceCount > 0)
        {
            total -= 10;
            aceCount--;
        }

        return total;
    }

    #endregion

    #region UI Yardımcıları ve Otomatik Masa / Menü Kurulumu

    private void UpdateScoreTexts()
    {
        if (playerScoreText != null)
        {
            int pTotal = CalculateHandValue(playerHand);
            playerScoreText.text = $"OYUNCU ELİ: {pTotal}";
        }

        if (dealerScoreText != null)
        {
            if (dealerHoleCardRevealed)
            {
                int dTotal = CalculateHandValue(dealerHand);
                dealerScoreText.text = $"KURPİYER ELİ: {dTotal}";
            }
            else if (dealerHand.Count >= 2)
            {
                dealerScoreText.text = $"KURPİYER ELİ: {dealerHand[0].BaseValue} + ?";
            }
            else if (dealerHand.Count == 1)
            {
                dealerScoreText.text = $"KURPİYER ELİ: {dealerHand[0].BaseValue}";
            }
            else
            {
                dealerScoreText.text = "KURPİYER ELİ: 0";
            }
        }
    }

    private void ApplyCustomTableAndButtonSprites()
    {
        if (tableBackgroundImage != null && masaArkaPlanCizimi != null)
        {
            tableBackgroundImage.sprite = masaArkaPlanCizimi;
            tableBackgroundImage.color = Color.white;
        }

        if (startMenuPanel != null && girisEkraniArkaPlanCizimi != null)
        {
            Image startBg = startMenuPanel.GetComponent<Image>();
            if (startBg != null)
            {
                startBg.sprite = girisEkraniArkaPlanCizimi;
                startBg.color = Color.white;
            }
        }

        ApplyButtonCustomSprite(dealButton, dagitButonCizimi);
        ApplyButtonCustomSprite(hitButton, kartCekButonCizimi);
        ApplyButtonCustomSprite(standButton, durButonCizimi);
        ApplyButtonCustomSprite(settingsCornerButton, ayarlarButonCizimi);
    }

    private void ApplyButtonCustomSprite(Button btn, Sprite customSprite)
    {
        if (btn == null || customSprite == null) return;

        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = customSprite;
            img.color = Color.white;
            img.preserveAspect = true;
        }

        if (butonYazilariniGizle)
        {
            Text label = btn.GetComponentInChildren<Text>();
            if (label != null) label.gameObject.SetActive(false);
        }
    }

    private void ClearTableCards()
    {
        playerHand.Clear();
        dealerHand.Clear();
        dealerHiddenCardObj = null;
        dealerHiddenCardData = null;

        foreach (GameObject obj in playerCardObjects)
        {
            if (obj != null) Destroy(obj);
        }
        playerCardObjects.Clear();

        foreach (GameObject obj in dealerCardObjects)
        {
            if (obj != null) Destroy(obj);
        }
        dealerCardObjects.Clear();
    }

    private void SetButtonsInteractable(bool canBetAndDeal, bool canPlayHand)
    {
        if (dealButton != null) dealButton.interactable = canBetAndDeal;
        if (clearBetButton != null) clearBetButton.interactable = canBetAndDeal && (currentBet > 0);
        if (hitButton != null) hitButton.interactable = canPlayHand;
        if (standButton != null) standButton.interactable = canPlayHand;

        foreach (GameObject chipBtn in chipButtonObjects)
        {
            if (chipBtn != null)
            {
                Button b = chipBtn.GetComponent<Button>();
                if (b != null) b.interactable = canBetAndDeal;
            }
        }
    }

    private void EnsureLayoutSettings(Transform areaTransform)
    {
        if (areaTransform == null) return;
        HorizontalLayoutGroup layout = areaTransform.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }
    }

    private void LoadDefaultFont()
    {
        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null)
            defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    [ContextMenu("Sahneye Masa ve UI Kur (Kalıcı)")]
    public void BuildOrUpgradeCompleteUI()
    {
        LoadDefaultFont();

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem", typeof(EventSystem));
            Type newInputModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newInputModule != null)
                esObj.AddComponent(newInputModule);
            else
                esObj.AddComponent<StandaloneInputModule>();
        }

        GameObject oldCanvas = GameObject.Find("BlackjackCanvas");
        if (oldCanvas != null)
        {
            DestroyImmediate(oldCanvas);
        }

        GameObject canvasObj = new GameObject("BlackjackCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        // 1. MASA ARKA PLANI
        GameObject bgObj = CreateUIElement("TableBackground", canvasObj.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        tableBackgroundImage = bgObj.AddComponent<Image>();
        tableBackgroundImage.color = new Color(0.07f, 0.32f, 0.18f, 1f);

        // 2. SOL ÜST PARA VE BAHİS PANELİ
        GameObject moneyPanel = CreateUIElement("TopLeftMoneyPanel", bgObj.transform, new Vector2(0.02f, 0.82f), new Vector2(0.24f, 0.97f), Vector2.zero, Vector2.zero);
        Image moneyBg = moneyPanel.AddComponent<Image>();
        moneyBg.color = new Color(0.03f, 0.15f, 0.08f, 0.90f);
        Outline moneyOutline = moneyPanel.AddComponent<Outline>();
        moneyOutline.effectColor = new Color(0.95f, 0.78f, 0.22f, 0.8f);
        moneyOutline.effectDistance = new Vector2(2f, -2f);

        moneyText = CreateTextElement("MoneyText", moneyPanel.transform, new Vector2(0.06f, 0.50f), new Vector2(0.94f, 0.95f),
            $"PARA: {baslangicParasi}$", 24, TextAnchor.MiddleLeft, new Color(1f, 0.88f, 0.28f, 1f));

        currentBetText = CreateTextElement("CurrentBetText", moneyPanel.transform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.50f),
            "BAHİS: 0$", 20, TextAnchor.MiddleLeft, new Color(0.82f, 0.98f, 0.86f, 1f));

        // 3. SAĞ YAN KLASİK ÇİP ALMA PANELİ
        GameObject chipShopPanel = CreateUIElement("RightChipPanel", bgObj.transform, new Vector2(0.87f, 0.14f), new Vector2(0.98f, 0.83f), Vector2.zero, Vector2.zero);
        Image chipShopBg = chipShopPanel.AddComponent<Image>();
        chipShopBg.color = new Color(0.03f, 0.15f, 0.08f, 0.82f);

        CreateTextElement("ChipPanelTitle", chipShopPanel.transform, new Vector2(0.05f, 0.89f), new Vector2(0.95f, 0.98f),
            "ÇİP SEÇ", 18, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.35f, 1f));

        GameObject chipListContainer = CreateUIElement("RightChipList", chipShopPanel.transform, new Vector2(0.08f, 0.15f), new Vector2(0.92f, 0.88f), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup chipVLayout = chipListContainer.AddComponent<VerticalLayoutGroup>();
        chipVLayout.childAlignment = TextAnchor.MiddleCenter;
        chipVLayout.spacing = 12f;
        chipVLayout.childControlWidth = false;
        chipVLayout.childControlHeight = false;
        chipVLayout.childForceExpandWidth = false;
        chipVLayout.childForceExpandHeight = false;
        rightChipBarArea = chipListContainer.transform;

        clearBetButton = CreateButtonElement("ClearBetButton", chipShopPanel.transform, new Vector2(0.08f, 0.02f), new Vector2(0.92f, 0.12f),
            "BAHSİ SIFIRLA", new Color(0.55f, 0.22f, 0.22f, 1f), 14);

        // 4. ORTADA BAHİS ÇİP YIĞINI ALANI (Masaya Konulan Çipler)
        GameObject betPileObj = CreateUIElement("TableBetChipPileArea", bgObj.transform, new Vector2(0.04f, 0.42f), new Vector2(0.18f, 0.62f), Vector2.zero, Vector2.zero);
        tableBetChipPileArea = betPileObj.transform;

        // 5. KENARDA DURAN KAPALI KART DESTESİ (DeckPileArea)
        GameObject deckAreaObj = CreateUIElement("DeckPileArea", bgObj.transform, new Vector2(0.73f, 0.56f), new Vector2(0.85f, 0.83f), Vector2.zero, Vector2.zero);
        deckPileArea = deckAreaObj.transform;

        // 6. KURPİYER VE OYUNCU KART ALANLARI
        dealerScoreText = CreateTextElement("DealerScoreText", bgObj.transform, new Vector2(0.25f, 0.85f), new Vector2(0.73f, 0.94f),
            "KURPİYER ELİ: 0", 25, TextAnchor.MiddleCenter, Color.white);

        GameObject dealerAreaObj = CreateUIElement("DealerCardArea", bgObj.transform, new Vector2(0.20f, 0.56f), new Vector2(0.72f, 0.84f), Vector2.zero, Vector2.zero);
        HorizontalLayoutGroup dealerLayout = dealerAreaObj.AddComponent<HorizontalLayoutGroup>();
        dealerLayout.childAlignment = TextAnchor.MiddleCenter;
        dealerLayout.spacing = 14f;
        dealerLayout.childControlWidth = false;
        dealerLayout.childControlHeight = false;
        dealerLayout.childForceExpandWidth = false;
        dealerLayout.childForceExpandHeight = false;
        dealerCardArea = dealerAreaObj.transform;

        statusText = CreateTextElement("StatusText", bgObj.transform, new Vector2(0.20f, 0.45f), new Vector2(0.78f, 0.54f),
            "Hazır!", 25, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.4f, 1f));

        playerScoreText = CreateTextElement("PlayerScoreText", bgObj.transform, new Vector2(0.25f, 0.36f), new Vector2(0.73f, 0.44f),
            "OYUNCU ELİ: 0", 25, TextAnchor.MiddleCenter, Color.white);

        GameObject playerAreaObj = CreateUIElement("PlayerCardArea", bgObj.transform, new Vector2(0.20f, 0.13f), new Vector2(0.78f, 0.36f), Vector2.zero, Vector2.zero);
        HorizontalLayoutGroup playerLayout = playerAreaObj.AddComponent<HorizontalLayoutGroup>();
        playerLayout.childAlignment = TextAnchor.MiddleCenter;
        playerLayout.spacing = 14f;
        playerLayout.childControlWidth = false;
        playerLayout.childControlHeight = false;
        playerLayout.childForceExpandWidth = false;
        playerLayout.childForceExpandHeight = false;
        playerCardArea = playerAreaObj.transform;

        // 7. ALT OYUN BUTONLARI
        dealButton = CreateButtonElement("DealButton", bgObj.transform, new Vector2(0.16f, 0.02f), new Vector2(0.38f, 0.11f),
            "ELİ BAŞLAT (DAĞIT)", new Color(0.88f, 0.62f, 0.12f, 1f), 19);

        hitButton = CreateButtonElement("HitButton", bgObj.transform, new Vector2(0.41f, 0.02f), new Vector2(0.60f, 0.11f),
            "KART ÇEK (HIT)", new Color(0.18f, 0.58f, 0.85f, 1f), 19);

        standButton = CreateButtonElement("StandButton", bgObj.transform, new Vector2(0.63f, 0.02f), new Vector2(0.82f, 0.11f),
            "DUR (STAND)", new Color(0.82f, 0.24f, 0.22f, 1f), 19);

        // 8. GİRİŞ (START) MENÜSÜ PANELİ
        BuildStartMenuPanel(canvasObj.transform);

        // 9. OYUN BİTTİ (GAME OVER - PARA BİTTİ) PANELİ
        BuildGameOverModalPanel(canvasObj.transform);

        // 10. HER ZAMAN KENARDA DURAN AYARLAR BUTONU VE AYARLAR PENCERESİ (En üst katmanda)
        settingsCornerButton = CreateButtonElement("SettingsCornerButton", canvasObj.transform, new Vector2(0.86f, 0.89f), new Vector2(0.98f, 0.97f),
            "⚙ AYARLAR", new Color(0.16f, 0.22f, 0.28f, 0.95f), 18);

        BuildSettingsModalPanel(canvasObj.transform);

        EnsureDeckPileVisual();
        BuildRightSideChipButtons();
        ApplyCustomTableAndButtonSprites();
    }

    private void BuildStartMenuPanel(Transform canvasParent)
    {
        startMenuPanel = CreateUIElement("StartMenuPanel", canvasParent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image bg = startMenuPanel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.18f, 0.10f, 0.98f);

        CreateTextElement("GameTitle", startMenuPanel.transform, new Vector2(0.15f, 0.66f), new Vector2(0.85f, 0.84f),
            "♠ 21 BLACKJACK ♥", 54, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.32f, 1f));

        CreateTextElement("GameSubtitle", startMenuPanel.transform, new Vector2(0.20f, 0.56f), new Vector2(0.80f, 0.65f),
            $"Başlangıç Bakiyesi: {baslangicParasi}$  |  Klasik Çiplerle Masaya Oturan Kazanır", 22, TextAnchor.MiddleCenter, new Color(0.85f, 0.95f, 0.88f, 1f));

        Button startBtn = CreateButtonElement("StartGameButton", startMenuPanel.transform, new Vector2(0.35f, 0.38f), new Vector2(0.65f, 0.50f),
            "OYUNA BAŞLA (START)", new Color(0.18f, 0.68f, 0.32f, 1f), 24);
        startBtn.onClick.AddListener(OnStartGameButtonClicked);

        Button openSettingsBtn = CreateButtonElement("StartSettingsButton", startMenuPanel.transform, new Vector2(0.35f, 0.24f), new Vector2(0.65f, 0.34f),
            "AYARLAR (SES & MÜZİK)", new Color(0.22f, 0.45f, 0.72f, 1f), 20);
        openSettingsBtn.onClick.AddListener(() => { PlaySFX(butonTiklamaSesi); ToggleSettingsModal(); });

        Button quitBtn = CreateButtonElement("QuitGameButton", startMenuPanel.transform, new Vector2(0.35f, 0.11f), new Vector2(0.65f, 0.20f),
            "ÇIKIŞ", new Color(0.72f, 0.22f, 0.22f, 1f), 20);
        quitBtn.onClick.AddListener(OnQuitGameButtonClicked);
    }

    private void BuildSettingsModalPanel(Transform canvasParent)
    {
        settingsModalPanel = CreateUIElement("SettingsModalPanel", canvasParent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image overlayBg = settingsModalPanel.AddComponent<Image>();
        overlayBg.color = new Color(0f, 0f, 0f, 0.72f);

        GameObject window = CreateUIElement("SettingsWindow", settingsModalPanel.transform, new Vector2(0.26f, 0.18f), new Vector2(0.74f, 0.82f), Vector2.zero, Vector2.zero);
        Image winBg = window.AddComponent<Image>();
        winBg.color = new Color(0.08f, 0.22f, 0.14f, 0.98f);
        Outline winOutline = window.AddComponent<Outline>();
        winOutline.effectColor = new Color(1f, 0.85f, 0.3f, 0.9f);
        winOutline.effectDistance = new Vector2(3f, -3f);

        CreateTextElement("SettingsTitle", window.transform, new Vector2(0.1f, 0.82f), new Vector2(0.9f, 0.95f),
            "AYARLAR (SES & MÜZİK)", 28, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.32f, 1f));

        musicToggleButton = CreateButtonElement("MusicToggleButton", window.transform, new Vector2(0.15f, 0.62f), new Vector2(0.85f, 0.76f),
            "MÜZİK: AÇIK", new Color(0.18f, 0.62f, 0.32f, 1f), 20);
        musicToggleButton.onClick.AddListener(ToggleMusic);

        musicVolumeSlider = CreateSimpleSlider("MusicVolumeSlider", window.transform, new Vector2(0.15f, 0.52f), new Vector2(0.85f, 0.59f), muzikSesSeviyesi);
        musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);

        sfxToggleButton = CreateButtonElement("SFXToggleButton", window.transform, new Vector2(0.15f, 0.34f), new Vector2(0.85f, 0.48f),
            "SES EFEKTLERİ: AÇIK", new Color(0.18f, 0.62f, 0.32f, 1f), 20);
        sfxToggleButton.onClick.AddListener(ToggleSFX);

        sfxVolumeSlider = CreateSimpleSlider("SFXVolumeSlider", window.transform, new Vector2(0.15f, 0.24f), new Vector2(0.85f, 0.31f), efektSesSeviyesi);
        sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);

        Button closeBtn = CreateButtonElement("CloseSettingsButton", window.transform, new Vector2(0.30f, 0.06f), new Vector2(0.70f, 0.18f),
            "KAPAT / DEVAM ET", new Color(0.88f, 0.62f, 0.12f, 1f), 20);
        closeBtn.onClick.AddListener(() => { PlaySFX(butonTiklamaSesi); ToggleSettingsModal(); });

        settingsModalPanel.SetActive(false);
    }

    private void BuildGameOverModalPanel(Transform canvasParent)
    {
        gameOverModalPanel = CreateUIElement("GameOverModalPanel", canvasParent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image overlayBg = gameOverModalPanel.AddComponent<Image>();
        overlayBg.color = new Color(0f, 0f, 0f, 0.80f);

        GameObject window = CreateUIElement("GameOverWindow", gameOverModalPanel.transform, new Vector2(0.24f, 0.22f), new Vector2(0.76f, 0.78f), Vector2.zero, Vector2.zero);
        Image winBg = window.AddComponent<Image>();
        winBg.color = new Color(0.18f, 0.07f, 0.08f, 0.98f);
        Outline winOutline = window.AddComponent<Outline>();
        winOutline.effectColor = new Color(0.95f, 0.3f, 0.25f, 0.9f);
        winOutline.effectDistance = new Vector2(3f, -3f);

        CreateTextElement("GameOverTitle", window.transform, new Vector2(0.1f, 0.74f), new Vector2(0.9f, 0.92f),
            "OYUN BİTTİ - KAYBETTİN!", 34, TextAnchor.MiddleCenter, new Color(1f, 0.38f, 0.35f, 1f));

        gameOverMessageText = CreateTextElement("GameOverMessage", window.transform, new Vector2(0.1f, 0.42f), new Vector2(0.9f, 0.72f),
            "Tüm paran bitti! Yeniden 100$ ile başlamak ister misin?", 22, TextAnchor.MiddleCenter, Color.white);

        Button retryBtn = CreateButtonElement("RetryButton", window.transform, new Vector2(0.12f, 0.12f), new Vector2(0.52f, 0.30f),
            "YENİDEN OYNA (100$)", new Color(0.18f, 0.65f, 0.32f, 1f), 20);
        retryBtn.onClick.AddListener(OnRestartAfterGameOverClicked);

        Button quitBtn = CreateButtonElement("GameOverQuitButton", window.transform, new Vector2(0.56f, 0.12f), new Vector2(0.88f, 0.30f),
            "ÇIKIŞ YAP", new Color(0.75f, 0.22f, 0.22f, 1f), 20);
        quitBtn.onClick.AddListener(OnQuitGameButtonClicked);

        gameOverModalPanel.SetActive(false);
    }

    private GameObject CreateUIElement(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return obj;
    }

    private Text CreateTextElement(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string content, int fontSize, TextAnchor anchor, Color color)
    {
        GameObject obj = CreateUIElement(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        Text txt = obj.AddComponent<Text>();
        txt.font = defaultFont;
        txt.text = content;
        txt.fontSize = fontSize;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = anchor;
        txt.color = color;
        return txt;
    }

    private Button CreateButtonElement(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string label, Color bgColor, int fontSize = 20)
    {
        GameObject obj = CreateUIElement(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        Image img = obj.AddComponent<Image>();
        img.color = bgColor;

        Button btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;

        CreateTextElement("Label", obj.transform, Vector2.zero, Vector2.one, label, fontSize, TextAnchor.MiddleCenter, Color.white);
        return btn;
    }

    private Slider CreateSimpleSlider(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, float initialValue)
    {
        GameObject sliderObj = CreateUIElement(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;

        GameObject bg = CreateUIElement("Background", sliderObj.transform, new Vector2(0f, 0.3f), new Vector2(1f, 0.7f), Vector2.zero, Vector2.zero);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.25f, 0.22f, 1f);

        GameObject fillArea = CreateUIElement("Fill Area", sliderObj.transform, new Vector2(0f, 0.3f), new Vector2(1f, 0.7f), Vector2.zero, Vector2.zero);
        GameObject fill = CreateUIElement("Fill", fillArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.92f, 0.75f, 0.22f, 1f);

        GameObject handleArea = CreateUIElement("Handle Slide Area", sliderObj.transform, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f));
        GameObject handle = CreateUIElement("Handle", handleArea.transform, new Vector2(0f, 0.1f), new Vector2(0f, 0.9f), new Vector2(-10f, 0f), new Vector2(10f, 0f));
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;

        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handleImg;
        slider.value = initialValue;

        return slider;
    }

    #endregion
}
