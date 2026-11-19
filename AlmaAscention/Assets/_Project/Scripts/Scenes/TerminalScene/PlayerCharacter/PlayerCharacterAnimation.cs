using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerCharacterAnimation : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite[] walkFrames;
    [SerializeField, Min(1)] private float framesPerSecond = 12;
    [SerializeField, Min(.01f), Tooltip("이 속도일 때 설정한 FPS로 재생합니다. 실제 이동 속도에 맞춰 보폭을 조절합니다.")]
    private float referenceMoveSpeed = 3;
    [SerializeField] private bool spriteFacesRight;
    private float framePosition;

    public void SetMovement(Vector2 displacement, float deltaTime)
    {
        if (spriteRenderer == null) return;
        if (displacement.sqrMagnitude < .00000001f || deltaTime <= 0)
        {
            ResetPose();
            return;
        }
        if (Mathf.Abs(displacement.x) > .00001f)
            spriteRenderer.flipX = (displacement.x > 0) != spriteFacesRight;
        if (walkFrames == null || walkFrames.Length == 0) return;
        framePosition = (framePosition + displacement.magnitude * framesPerSecond / Mathf.Max(.01f, referenceMoveSpeed)) % walkFrames.Length;
        spriteRenderer.sprite = walkFrames[Mathf.FloorToInt(framePosition)];
    }

    private void OnDisable() => ResetPose();

    private void ResetPose()
    {
        framePosition = 0;
        if (spriteRenderer != null && idleSprite != null) spriteRenderer.sprite = idleSprite;
    }
}
