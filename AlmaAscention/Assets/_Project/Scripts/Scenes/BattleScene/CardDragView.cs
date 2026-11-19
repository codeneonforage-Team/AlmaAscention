using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using CNF.Data;

public sealed class CardDragView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public CardInstance Card { get; private set; }
    public Vector2 HomePosition { get; set; }
    public float HomeAngle { get; set; }
    private CardHandUI hand;
    private CardUI cardUI;
    private RectTransform rect;
    private Vector2 targetPosition, positionVelocity;
    private float targetAngle, angleVelocity, targetScale = 1, scaleVelocity;
    private bool hasPose;
    public void Initialize(CardHandUI owner, CardInstance card, CardUI visual)
    { hand=owner;Card=card;cardUI=visual;rect=(RectTransform)transform; }
    public void Refresh(CNF.AbilitySystem.CombatContext context, bool playable) => cardUI.Display(Card, context, playable);
    public void SetPose(Vector2 position, float angle, float scale, bool immediate = false)
    {
        targetPosition=position;targetAngle=angle;targetScale=scale;
        if (!hasPose || immediate)
        {
            rect.anchoredPosition=position;rect.localRotation=Quaternion.Euler(0,0,angle);rect.localScale=Vector3.one*scale;
            positionVelocity=Vector2.zero;angleVelocity=scaleVelocity=0;
        }
        hasPose=true;
    }
    private void LateUpdate()
    {
        if (!hasPose || hand == null) return;
        float duration=Mathf.Max(.01f,hand.HoverTransitionTime);
        float dt=Time.unscaledDeltaTime;
        rect.anchoredPosition=Vector2.SmoothDamp(rect.anchoredPosition,targetPosition,ref positionVelocity,duration,Mathf.Infinity,dt);
        float angle=Mathf.SmoothDampAngle(rect.localEulerAngles.z,targetAngle,ref angleVelocity,duration,Mathf.Infinity,dt);
        rect.localRotation=Quaternion.Euler(0,0,angle);
        float scale=Mathf.SmoothDamp(rect.localScale.x,targetScale,ref scaleVelocity,duration,Mathf.Infinity,dt);
        rect.localScale=Vector3.one*scale;
    }
    public void OnPointerEnter(PointerEventData e) { if(hand!=null) hand.Hover(this,true); }
    public void OnPointerExit(PointerEventData e) { if(hand!=null) hand.Hover(this,false); }
    public void OnBeginDrag(PointerEventData e) { if(hand!=null && e.button==PointerEventData.InputButton.Left && hand.BeginDrag(this))hand.Drag(e.position,e.pressEventCamera); }
    public void OnDrag(PointerEventData e) { if(hand!=null && e.button==PointerEventData.InputButton.Left)hand.Drag(e.position,e.pressEventCamera); }
    public void OnEndDrag(PointerEventData e) { if(hand!=null && e.button==PointerEventData.InputButton.Left)hand.EndDrag(this,e.position,e.pressEventCamera); }
}
