using UnityEngine;
using UnityEngine.EventSystems;

public sealed class TitleMenuSelection : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private GameObject indicator;
    public void OnPointerEnter(PointerEventData eventData)
    {
        var button = GetComponent<UnityEngine.UI.Button>();
        if (button != null && button.IsInteractable() && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(gameObject);
    }
    public void OnSelect(BaseEventData eventData) { if (indicator != null) indicator.SetActive(true); }
    public void OnDeselect(BaseEventData eventData) { if (indicator != null) indicator.SetActive(false); }
}
