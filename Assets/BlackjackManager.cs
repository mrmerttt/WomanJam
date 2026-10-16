using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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

public class BlackjackManager : MonoBehaviour
{
    [Header("Gizli Olasılık Ayarları (Sadece Inspector'dan Ayarlanır)")]
    [Tooltip("Oyuncunun kazanma yüzdesi. Oyun içinde görünmez, sadece buradan kontrol edilir.")]
    [Range(0f, 100f)]
    public float playerWinChance = 40f;

    [Tooltip("Açıkken %40/%60 hedef sonucuna uygun akıllı kart seçilir. Kapatılırsa %100 saf deste matematiği çalışır.")]
    public bool useControlledOdds = true;

    [Header("Hafif Kart Animasyonu ve Kenar Deste Ayarları")]
    [Tooltip("Kartların açılırken dönme (Flip) animasyonu olsun mu?")]
    public bool kartDonmeAnimasyonu = true;

    [Tooltip("Bilgisayarı yormaması için animasyonun saniyedeki kare sayısı (12-20 arası tatlı retro/hafif bir his verir).")]
    [Range(8, 60)]
    public int animasyonFps = 16;

    [Tooltip("Bir kartın dönerek açılma süresi (saniye)")]
    [Range(0.1f, 0.6f)]
    public float kartDonmeSuresi = 0.24f;

    [Tooltip("Masanın kenarında sabit duran kapalı kart destesi (yığını) görünsün mü?")]
    public bool kenardaKapaliDesteGoster = true;

    [Header("Masa ve Buton Çizimlerin (İsteğe Bağlı Sürükle-Bırak)")]
    [Tooltip("Kendi çizdiğin masa/arka plan görseli")]
    public Sprite masaArkaPlanCizimi;

    [Tooltip("Yeni El Dağıt butonu için kendi çizimin")]
    public Sprite dagitButonCizimi;

    [Tooltip("Kart Çek (Hit) butonu için kendi çizimin")]
    public Sprite kartCekButonCizimi;

    [Tooltip("Dur (Stand) butonu için kendi çizimin")]
    public Sprite durButonCizimi;

    [Tooltip("Buton çiziminin üzerinde kendi yazın varsa bunu işaretle, otomatik buton yazılarını gizler.")]
    public bool butonYazilariniGizle = false;

    [Header("Kart Çizimlerin (Sürükle-Bırak)")]
    [Tooltip("Kurpiyerin kapalı kartı ve kenardaki deste için kart arkası çizimin")]
    public Sprite kartArkaYuzCizimi;

    [Tooltip("52 kartı tek tek çizmek yerine tek bir boş kart şablonu çizdiysen buraya atabilirsin (üzerine sayıları otomatik yazar).")]
    public Sprite bosKartOnYuzCizimi;

    [Tooltip("Kartların ekrandaki genişlik ve yükseklik boyutu (piksel)")]
    public Vector2 kartBoyutu = new Vector2(120f, 175f);

    [Tooltip("Kupa kart çizimlerin (Sırasıyla 13 adet: As, 2, 3, 4, 5, 6, 7, 8, 9, 10, J, Q, K)")]
    public List<Sprite> kupaKartlari = new List<Sprite>();

    [Tooltip("Karo kart çizimlerin (Sırasıyla 13 adet: As, 2, 3, 4, 5, 6, 7, 8, 9, 10, J, Q, K)")]
    public List<Sprite> karoKartlari = new List<Sprite>();

    [Tooltip("Maça kart çizimlerin (Sırasıyla 13 adet: As, 2, 3, 4, 5, 6, 7, 8, 9, 10, J, Q, K)")]
    public List<Sprite> macaKartlari = new List<Sprite>();

    [Tooltip("Sinek kart çizimlerin (Sırasıyla 13 adet: As, 2, 3, 4, 5, 6, 7, 8, 9, 10, J, Q, K)")]
    public List<Sprite> sinekKartlari = new List<Sprite>();

    [Header("Sahne ve UI Bağlantıları")]
    [Tooltip("Açık bırakırsan sahnede eksik olan UI elemanlarını otomatik kurar.")]
    public bool autoCreateUI = true;

    public Image tableBackgroundImage;
    public Text dealerScoreText;
    public Text playerScoreText;
    public Text statusText;

    public Button dealButton;
    public Button hitButton;
    public Button standButton;

    public Transform dealerCardArea;
    public Transform playerCardArea;
    public Transform deckPileArea; // Kenarda duran kapalı kart destesi yeri

    // Oyun içi listeler
    private List<Card> deck = new List<Card>();
    private List<Card> playerHand = new List<Card>();
    private List<Card> dealerHand = new List<Card>();

    private List<GameObject> playerCardObjects = new List<GameObject>();
    private List<GameObject> dealerCardObjects = new List<GameObject>();
    private List<GameObject> deckPileObjects = new List<GameObject>();

    private GameObject dealerHiddenCardObj;
    private Card dealerHiddenCardData;

    private bool isRoundActive = false;
    private bool isDealingOrAnimating = false;
    private bool dealerHoleCardRevealed = false;
    private bool targetPlayerWinThisRound = false;

    private Font defaultFont;

    private void Start()
    {
        LoadDefaultFont();

        if (autoCreateUI && dealButton == null)
        {
            BuildCompleteTableUI();
        }

        EnsureLayoutSettings(playerCardArea);
        EnsureLayoutSettings(dealerCardArea);
        EnsureDeckPileVisual();
        ApplyCustomTableAndButtonSprites();

        if (dealButton != null)
        {
            dealButton.onClick.RemoveAllListeners();
            dealButton.onClick.AddListener(StartNewRound);
        }
        if (hitButton != null)
        {
            hitButton.onClick.RemoveAllListeners();
            hitButton.onClick.AddListener(OnHitClicked);
        }
        if (standButton != null)
        {
            standButton.onClick.RemoveAllListeners();
            standButton.onClick.AddListener(OnStandClicked);
        }

        SetButtonsInteractable(canDeal: true, canPlay: false);

        if (statusText != null)
            statusText.text = "Başlamak için 'YENİ EL DAĞIT' butonuna bas.";
    }

    public void SetWinChance(float newWinPercentage)
    {
        playerWinChance = Mathf.Clamp(newWinPercentage, 0f, 100f);
    }

    #region Oyun Akışı (Sıralı Dağıtım, Hit, Stand)

    public void StartNewRound()
    {
        if (isDealingOrAnimating) return;

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
        SetButtonsInteractable(canDeal: false, canPlay: false);

        if (statusText != null)
            statusText.text = "Kartlar dağıtılıyor...";

        UpdateScoreTexts();

        // 1. Oyuncunun 1. kartı
        Card pCard1 = DrawCardForInitialDeal(forPlayer: true);
        playerHand.Add(pCard1);
        yield return StartCoroutine(SpawnAndFlipCardRoutine(pCard1, playerCardArea, playerCardObjects, stayHidden: false));
        UpdateScoreTexts();

        // 2. Kurpiyerin 1. kartı (Açık)
        Card dCard1 = DrawCardForInitialDeal(forPlayer: false);
        dealerHand.Add(dCard1);
        yield return StartCoroutine(SpawnAndFlipCardRoutine(dCard1, dealerCardArea, dealerCardObjects, stayHidden: false));
        UpdateScoreTexts();

        // 3. Oyuncunun 2. kartı
        Card pCard2 = DrawCardForInitialDeal(forPlayer: true);
        playerHand.Add(pCard2);
        yield return StartCoroutine(SpawnAndFlipCardRoutine(pCard2, playerCardArea, playerCardObjects, stayHidden: false));
        UpdateScoreTexts();

        // 4. Kurpiyerin 2. kartı (Kapalı durur, sıra kurpiyere gelince dönerek açılır)
        Card dCard2 = DrawCardForInitialDeal(forPlayer: false);
        dealerHand.Add(dCard2);
        dealerHiddenCardData = dCard2;
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

        SetButtonsInteractable(canDeal: false, canPlay: true);
    }

    public void OnHitClicked()
    {
        if (!isRoundActive || isDealingOrAnimating) return;

        StartCoroutine(PlayerHitRoutine());
    }

    private IEnumerator PlayerHitRoutine()
    {
        isDealingOrAnimating = true;
        SetButtonsInteractable(canDeal: false, canPlay: false);

        Card drawnCard = DrawSmartCardForPlayer();
        playerHand.Add(drawnCard);

        // Mevcut kartları silmeden sadece yeni kartı ekleyip döndürerek aç
        yield return StartCoroutine(SpawnAndFlipCardRoutine(drawnCard, playerCardArea, playerCardObjects, stayHidden: false));
        UpdateScoreTexts();

        int playerScore = CalculateHandValue(playerHand);

        if (playerScore > 21)
        {
            // Oyuncu 21'i geçtiyse kurpiyerin kapalı kartını da döndürerek aç
            if (!dealerHoleCardRevealed && dealerHiddenCardObj != null)
            {
                dealerHoleCardRevealed = true;
                yield return StartCoroutine(FlipExistingCardOpenRoutine(dealerHiddenCardObj, dealerHiddenCardData));
                UpdateScoreTexts();
            }

            isDealingOrAnimating = false;
            EndRound($"21'i Geçtin ({playerScore})! Kasa Kazandı.");
        }
        else if (playerScore == 21)
        {
            isDealingOrAnimating = false;
            OnStandClicked();
        }
        else
        {
            isDealingOrAnimating = false;
            SetButtonsInteractable(canDeal: false, canPlay: true);
        }
    }

    public void OnStandClicked()
    {
        if (!isRoundActive || isDealingOrAnimating) return;

        SetButtonsInteractable(canDeal: false, canPlay: false);
        StartCoroutine(DealerTurnRoutine());
    }

    private IEnumerator DealerTurnRoutine()
    {
        isDealingOrAnimating = true;

        if (statusText != null)
            statusText.text = "Kurpiyer kapalı kartını açıyor...";

        // Kurpiyerin kapalı duran 2. kartını kendi yerinde döndürerek aç
        if (!dealerHoleCardRevealed)
        {
            dealerHoleCardRevealed = true;
            if (dealerHiddenCardObj != null)
            {
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
            EndRound($"Kurpiyer 21'i Geçti ({dealerScore})! KAZANDIN!");
        }
        else if (playerScore > dealerScore)
        {
            EndRound($"KAZANDIN! ({playerScore} - {dealerScore})");
        }
        else if (playerScore < dealerScore)
        {
            EndRound($"KASA KAZANDI! ({dealerScore} - {playerScore})");
        }
        else
        {
            EndRound($"BERABERE! ({playerScore} - {dealerScore})");
        }
    }

    private void EndRound(string reason)
    {
        isRoundActive = false;
        isDealingOrAnimating = false;
        SetButtonsInteractable(canDeal: true, canPlay: false);

        if (statusText != null)
            statusText.text = reason;
    }

    #endregion

    #region Hafif (Düşük FPS) Kart Dönme Animasyonu ve Görsel Oluşturma

    /// <summary>
    /// Yeni bir kartı önce kapalı oluşturur, ardından sistemi yormayan düşük FPS'li Scale-X dönüşüyle ön yüzünü açar.
    /// </summary>
    private IEnumerator SpawnAndFlipCardRoutine(Card card, Transform parentArea, List<GameObject> targetList, bool stayHidden)
    {
        if (parentArea == null) yield break;

        // Kartı başlangıçta kapalı (arka yüzü dönük) oluştur
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

    /// <summary>
    /// Kapalı duran bir kartı kendi yerinde yatayda döndürerek (1 -> 0 -> 1) açık yüzüne çevirir.
    /// WaitForSeconds(1f / animasyonFps) kullanarak bilgisayarı hiç yormaz.
    /// </summary>
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

        // 1. Aşama: Kart kapalıyken yatayda daralarak 90 dereceye (Scale X = 0) döner
        for (int i = 1; i <= stepsPerHalf; i++)
        {
            if (rt == null) yield break;
            float t = (float)i / stepsPerHalf;
            rt.localScale = new Vector3(Mathf.Lerp(1f, 0f, t), 1f, 1f);
            yield return new WaitForSeconds(stepTime);
        }

        // Tam ortada (kart dikey çizgi halindeyken) görseli açık kart yüzüyle değiştir
        if (cardObj != null)
        {
            ApplyCardVisualFace(cardObj, card, isHidden: false);
        }

        // 2. Aşama: Açık yüzüyle tekrar tam genişliğe (Scale X = 1) döner
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

        // LayoutElement sayesinde birkaç kart açıldığında kartların küçülmesi veya kaybolması tamamen engellenir
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

        // 1. Durum: Kart kapalıysa
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

        // 2. Durum: Kart açıksa ve o karta ait özel çizim (kupaKartlari vb.) varsa
        if (card.sprite != null)
        {
            bgImage.sprite = card.sprite;
            bgImage.color = Color.white;
            if (outline != null) outline.enabled = false;
            if (txt != null) txt.text = "";
            return;
        }

        // 3. Durum: Tek bir boş kart şablonu varsa veya varsayılan kart kutusu kullanılıyorsa
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

    /// <summary>
    /// Masanın kenarında sabit duran kapalı kart destesi görünümünü oluşturur.
    /// </summary>
    private void EnsureDeckPileVisual()
    {
        if (!kenardaKapaliDesteGoster)
        {
            if (deckPileArea != null) deckPileArea.gameObject.SetActive(false);
            return;
        }

        // Eğer sahnede önceden kurulmuş bir Canvas varsa ama DeckPileArea yoksa otomatik ekle
        if (deckPileArea == null && tableBackgroundImage != null)
        {
            GameObject deckAreaObj = CreateUIElement("DeckPileArea", tableBackgroundImage.transform,
                new Vector2(0.84f, 0.42f), new Vector2(0.96f, 0.68f), Vector2.zero, Vector2.zero);
            deckPileArea = deckAreaObj.transform;
        }

        if (deckPileArea == null) return;
        deckPileArea.gameObject.SetActive(true);

        foreach (GameObject obj in deckPileObjects)
        {
            if (obj != null) Destroy(obj);
        }
        deckPileObjects.Clear();

        // Üst üste duran 3 adet kapalı kart ile deste derinliği ver
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

    #region UI Yardımcıları ve Otomatik Masa Kurulumu

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

        ApplyButtonCustomSprite(dealButton, dagitButonCizimi);
        ApplyButtonCustomSprite(hitButton, kartCekButonCizimi);
        ApplyButtonCustomSprite(standButton, durButonCizimi);
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

    private void SetButtonsInteractable(bool canDeal, bool canPlay)
    {
        if (dealButton != null) dealButton.interactable = canDeal;
        if (hitButton != null) hitButton.interactable = canPlay;
        if (standButton != null) standButton.interactable = canPlay;
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
    public void BuildCompleteTableUI()
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

        // Eski otomatik canvas varsa temizle ki üst üste binmesin
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

        // Masa Arka Planı
        GameObject bgObj = CreateUIElement("TableBackground", canvasObj.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        tableBackgroundImage = bgObj.AddComponent<Image>();
        tableBackgroundImage.color = new Color(0.07f, 0.32f, 0.18f, 1f);

        // Kenarda Duran Kapalı Kart Destesi Alanı (DeckPileArea)
        GameObject deckAreaObj = CreateUIElement("DeckPileArea", bgObj.transform, new Vector2(0.84f, 0.42f), new Vector2(0.96f, 0.68f), Vector2.zero, Vector2.zero);
        deckPileArea = deckAreaObj.transform;

        // Kurpiyer Başlığı ve Kart Alanı
        dealerScoreText = CreateTextElement("DealerScoreText", bgObj.transform, new Vector2(0.1f, 0.84f), new Vector2(0.82f, 0.93f),
            "KURPİYER ELİ: 0", 26, TextAnchor.MiddleCenter, Color.white);

        GameObject dealerAreaObj = CreateUIElement("DealerCardArea", bgObj.transform, new Vector2(0.08f, 0.56f), new Vector2(0.82f, 0.83f), Vector2.zero, Vector2.zero);
        HorizontalLayoutGroup dealerLayout = dealerAreaObj.AddComponent<HorizontalLayoutGroup>();
        dealerLayout.childAlignment = TextAnchor.MiddleCenter;
        dealerLayout.spacing = 16f;
        dealerLayout.childControlWidth = false;
        dealerLayout.childControlHeight = false;
        dealerLayout.childForceExpandWidth = false;
        dealerLayout.childForceExpandHeight = false;
        dealerCardArea = dealerAreaObj.transform;

        // Orta Durum Mesajı
        statusText = CreateTextElement("StatusText", bgObj.transform, new Vector2(0.1f, 0.45f), new Vector2(0.82f, 0.54f),
            "Hazır!", 28, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.4f, 1f));

        // Oyuncu Başlığı ve Kart Alanı
        playerScoreText = CreateTextElement("PlayerScoreText", bgObj.transform, new Vector2(0.1f, 0.36f), new Vector2(0.82f, 0.44f),
            "OYUNCU ELİ: 0", 26, TextAnchor.MiddleCenter, Color.white);

        GameObject playerAreaObj = CreateUIElement("PlayerCardArea", bgObj.transform, new Vector2(0.08f, 0.13f), new Vector2(0.82f, 0.36f), Vector2.zero, Vector2.zero);
        HorizontalLayoutGroup playerLayout = playerAreaObj.AddComponent<HorizontalLayoutGroup>();
        playerLayout.childAlignment = TextAnchor.MiddleCenter;
        playerLayout.spacing = 16f;
        playerLayout.childControlWidth = false;
        playerLayout.childControlHeight = false;
        playerLayout.childForceExpandWidth = false;
        playerLayout.childForceExpandHeight = false;
        playerCardArea = playerAreaObj.transform;

        // Alt Butonlar
        dealButton = CreateButtonElement("DealButton", bgObj.transform, new Vector2(0.15f, 0.02f), new Vector2(0.37f, 0.11f),
            "YENİ EL DAĞIT", new Color(0.88f, 0.62f, 0.12f, 1f));

        hitButton = CreateButtonElement("HitButton", bgObj.transform, new Vector2(0.40f, 0.02f), new Vector2(0.59f, 0.11f),
            "KART ÇEK (HIT)", new Color(0.18f, 0.58f, 0.85f, 1f));

        standButton = CreateButtonElement("StandButton", bgObj.transform, new Vector2(0.62f, 0.02f), new Vector2(0.81f, 0.11f),
            "DUR (STAND)", new Color(0.82f, 0.24f, 0.22f, 1f));

        EnsureDeckPileVisual();
        ApplyCustomTableAndButtonSprites();
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

    private Button CreateButtonElement(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string label, Color bgColor)
    {
        GameObject obj = CreateUIElement(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        Image img = obj.AddComponent<Image>();
        img.color = bgColor;

        Button btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;

        CreateTextElement("Label", obj.transform, Vector2.zero, Vector2.one, label, 20, TextAnchor.MiddleCenter, Color.white);
        return btn;
    }

    #endregion
}