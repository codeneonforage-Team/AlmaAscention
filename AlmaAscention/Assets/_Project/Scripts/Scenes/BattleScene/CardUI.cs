using UnityEngine;
using TMPro;
using CNF.Data;
using CNF.AbilitySystem;

/// <summary>A single card's presentation. Layout and input belong to other components.</summary>
[ExecuteAlways, RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
public sealed class CardUI : MonoBehaviour
{
    [Header("Text Slots")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text descriptionText;
    [Header("Image Slots")]
    [SerializeField] private UnityEngine.UI.Image frameImage;
    [SerializeField] private UnityEngine.UI.Image artworkImage;
    [SerializeField, Tooltip("카드 데이터에 그림이 없을 때 표시할 기본 이미지")] private Sprite defaultArtwork;
    [Header("Appearance")]
    [SerializeField, Range(0,1)] private float disabledAlpha = .55f;
    [Header("Editor Preview")]
    [SerializeField] private CardDataSO previewCard;
    private CanvasGroup group;
    private Sprite templateArtwork;
    private void OnEnable()
    {
        if (artworkImage != null) templateArtwork = artworkImage.sprite;
    }

    public void Display(CardInstance card, CombatContext context, bool playable)
    {
        if (card == null || card.Data == null) return;
        Set(nameText, card.DisplayName);
        Set(costText, card.IsXCost ? "X" : card.CurrentCost.ToString());
        Set(descriptionText, card.GetDynamicDescription(context));
        SetArt(card.Data.CardArt);
        if (group == null) group = GetComponent<CanvasGroup>();
        group.alpha = playable ? 1 : disabledAlpha;
    }
    private static void Set(TMP_Text label, string text) { if(label != null && label.text != text) label.text = text; }
    private void SetArt(Sprite art)
    {
        if (artworkImage == null) return;
        artworkImage.sprite = art != null ? art : defaultArtwork != null ? defaultArtwork : templateArtwork;
        artworkImage.enabled = artworkImage.sprite != null;
    }
#if UNITY_EDITOR
    private void Update()
    {
        if (Application.IsPlaying(gameObject) || previewCard == null) return;
        Set(nameText, previewCard.CardName);
        Set(costText, previewCard.IsXCost || previewCard.BaseCost < 0 ? "X" : previewCard.BaseCost.ToString());
        Set(descriptionText, previewCard.GetFormattedBaseDescription());
        SetArt(previewCard.CardArt);
    }
#endif
}
