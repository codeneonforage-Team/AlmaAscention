using System.Collections.Generic;
using UnityEngine;
using TMPro;
using CNF.Data;
using CNF.AbilitySystem;
using AlmaAscention.Combat;

/// <summary>Fan-shaped hand presentation; card rules
/// remain in the battle systems.</summary>
[ExecuteAlways]
public sealed class CardHandUI : MonoBehaviour
{
    [Header("Hand Layout")]
    [SerializeField, Tooltip("1920 × 1080 기준 카드 너비와 높이")] private Vector2 cardSize = new Vector2(230, 280);
    [SerializeField, Min(1), Tooltip("카드 중심이 놓이는 원의 반지름. 작을수록 더 둥글게 펼쳐집니다.")] private float radius = 1800;
    [SerializeField, Range(0, 170), Tooltip("손패 전체가 펼쳐질 수 있는 최대 원호 각도")] private float maxArcAngle = 140;
    [SerializeField, Min(0), Tooltip("인접 카드 중심 사이의 원호 길이. 전체 너비·각도 제한을 넘으면 축소됩니다.")] private float spacing = 200;
    [SerializeField, Tooltip("화면 중앙 기준 원호 꼭대기의 Y 위치. 반지름이 바뀌어도 꼭대기 높이는 유지됩니다.")] private float handHeight = -320;
    [SerializeField, Min(1)] private float maxHandWidth = 1200;
    [SerializeField] private float hoverLift = 80;
    [SerializeField, Min(.01f), Tooltip("호버 상승·복귀 시 부드러움의 기준 시간(초). 작을수록 빠릅니다.")] private float hoverTransitionTime = .12f;
    public float HoverTransitionTime => hoverTransitionTime;
    [Header("Prefab References")]
    [SerializeField] private CardUI cardPrefab;
    [SerializeField] private RectTransform dropArea;
    [Header("Editor Preview")]
    [SerializeField] private bool showPreview = true;
    [SerializeField, Range(1, 12)] private int previewCardCount = 5;
    private readonly List<CardDragView> views = new();
    private SimpleBattleScene battle;
    private RectTransform root;
    private UnityEngine.UI.Image dropImage;
    private CardDragView hovered, dragged;
    private bool refreshCanvasOnFirstHand;
    public bool IsDragging => dragged != null;

    public void Initialize(SimpleBattleScene owner)
    {
        battle = owner;
        root = (RectTransform)transform;
        refreshCanvasOnFirstHand = true;
        dropImage = dropArea.GetComponent<UnityEngine.UI.Image>();
        dropArea.gameObject.SetActive(false);
    }

    public void Refresh(CombatContext context)
    {
        var hand = battle.Flow.DeckManager.HandCards;
        for (int i = views.Count - 1; i >= 0; i--)
        {
            if (Contains(hand, views[i].Card)) continue;
            var view = views[i];
            if (hovered == view) hovered = null;
            if (dragged == view) { dragged = null; dropArea.gameObject.SetActive(false); }
            views.RemoveAt(i); view.gameObject.SetActive(false); Destroy(view.gameObject);
        }
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            var view = views.Find(v => v.Card == card);
            if (view == null)
            {
                var cardView = Instantiate(cardPrefab, root); cardView.name = "Card_" + card.InstanceGUID; cardView.gameObject.SetActive(true);
                view = cardView.GetComponent<CardDragView>();
                view.Initialize(this, card, cardView);
                views.Add(view);
            }
            view.Refresh(context, CanUse(card));
            ((RectTransform)view.transform).sizeDelta = cardSize;
            CalculateCardPose(i, hand.Count, out var position, out var rotation);
            view.HomePosition = position;
            view.HomeAngle = rotation;
            if (view != dragged && view != hovered) view.SetPose(view.HomePosition, view.HomeAngle, 1);
        }
        if (hovered != null && hovered != dragged) hovered.SetPose(hovered.HomePosition + Vector2.up * hoverLift, 0, 1.08f);
        if (refreshCanvasOnFirstHand && views.Count > 0)
        {
            // Rebuild the scene Canvas after replacing its editor preview with runtime cards.
            var canvas = GetComponent<Canvas>();
            canvas.enabled = false; canvas.enabled = true;
            Canvas.ForceUpdateCanvases(); refreshCanvasOnFirstHand = false;
        }
    }
    public void CalculateCardPose(int index, int count, out Vector2 position, out float rotation)
    {
        float r = Mathf.Max(1, radius);
        int intervals = Mathf.Max(1, count - 1);
        float widthAngle = 2 * Mathf.Asin(Mathf.Clamp01(Mathf.Max(0, maxHandWidth) / (2 * r)));
        float totalAngle = Mathf.Min(Mathf.Max(0, spacing) / r * Mathf.Max(0, count - 1),
            Mathf.Clamp(maxArcAngle, 0, 170) * Mathf.Deg2Rad, widthAngle);
        float angle = count <= 1 ? 0 : (index - (count - 1) * .5f) * totalAngle / intervals;
        // Circle center: (0, handHeight - radius). Card top points radially outward.
        position = new Vector2(r * Mathf.Sin(angle), handHeight + r * (Mathf.Cos(angle) - 1));
        rotation = -angle * Mathf.Rad2Deg;
    }
    private static bool Contains(IReadOnlyList<CardInstance> cards, CardInstance card)
    { for (int i = 0; i < cards.Count; i++) if (cards[i] == card) return true; return false; }

    public bool CanUse(CardInstance card) => battle != null && battle.Flow != null
        && !SceneFadeTransition.IsTransitioning && battle.Flow.State == EBattleState.PlayerTurn
        && !battle.Flow.CardExecutor.IsBusy && Contains(battle.Flow.DeckManager.HandCards, card)
        && battle.Flow.CardExecutor.CanPlayCard(card, new CombatContext { Caster = battle.Flow.Player, PrimaryTarget = battle.Enemy }, out _);

    public void Hover(CardDragView view, bool enter)
    {
        if (dragged != null) return;
        if (enter) { hovered = view; view.transform.SetAsLastSibling(); }
        else if (hovered == view) { hovered = null; RestoreOrder(); }
    }
    public bool BeginDrag(CardDragView view)
    {
        if (dragged != null || !CanUse(view.Card)) return false;
        dragged = view; hovered = null; view.transform.SetAsLastSibling();
        dropArea.gameObject.SetActive(true); return true;
    }
    public bool IsValidDrop(Vector2 screen, Camera camera) => RectTransformUtility.RectangleContainsScreenPoint(dropArea, screen, camera);
    public void Drag(Vector2 screen, Camera camera)
    {
        if (dragged == null) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, camera, out var point))
            dragged.SetPose(point, 0, 1.08f, immediate: true);
        dropImage.color = IsValidDrop(screen, camera) ? new Color(.35f,.62f,.6f,.32f) : new Color(.2f,.15f,.35f,.28f);
    }
    public void EndDrag(CardDragView view, Vector2 screen, Camera camera)
    {
        if (dragged != view) return;
        bool valid = IsValidDrop(screen, camera) && CanUse(view.Card);
        CancelDrag();
        if (valid) battle.PlayCard(view.Card);
    }
    public void CancelDrag()
    {
        if (dragged != null) dragged.SetPose(dragged.HomePosition, dragged.HomeAngle, 1);
        dragged = null; hovered = null;
        if (dropArea != null) dropArea.gameObject.SetActive(false);
        RestoreOrder();
    }
    private void RestoreOrder() { foreach (var view in views) if (view != null) view.transform.SetAsLastSibling(); }
    private void OnApplicationFocus(bool focus) { if (!focus) CancelDrag(); }
    private void OnDisable()
    {
        // Unity forbids reordering children while a parent is being deactivated.
        dragged = null; hovered = null;
#if UNITY_EDITOR
        ClearPreview();
#endif
    }
#if UNITY_EDITOR
    private readonly List<RectTransform> previewCards = new();
    private void OnEnable()
    {
        ClearPreview();
        if (dropArea != null) dropArea.gameObject.SetActive(false);
    }
    private void OnValidate()
    {
        cardSize = new Vector2(Mathf.Max(1, cardSize.x), Mathf.Max(1, cardSize.y));
        spacing = Mathf.Max(0, spacing); maxHandWidth = Mathf.Max(1, maxHandWidth);
        radius = Mathf.Max(1, radius);
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
    }
    private void Update()
    {
        if (Application.IsPlaying(gameObject) || UnityEditor.EditorUtility.IsPersistent(this)) return;
        if (!showPreview || cardPrefab == null) { ClearPreview(); return; }
        if (previewCards.Count != previewCardCount)
        {
            ClearPreview();
            for (int i = 0; i < previewCardCount; i++)
            {
                var rect = (RectTransform)Instantiate(cardPrefab, transform).transform; rect.name = "__CardPreview" + i;
                rect.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                rect.gameObject.SetActive(true); previewCards.Add(rect);
            }
        }
        for (int i = 0; i < previewCards.Count; i++)
        {
            var rect = previewCards[i];
            CalculateCardPose(i, previewCards.Count, out var position, out var rotation);
            rect.sizeDelta = cardSize; rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0,0,rotation);rect.localScale=Vector3.one;
        }
    }
    private void ClearPreview()
    {
        previewCards.Clear();
        for(int i=transform.childCount-1;i>=0;i--)
        {
            var child=transform.GetChild(i).gameObject;
            if(child.name.StartsWith("__CardPreview") && (child.hideFlags & HideFlags.DontSaveInEditor)!=0) DestroyImmediate(child);
        }
    }
#endif
}
