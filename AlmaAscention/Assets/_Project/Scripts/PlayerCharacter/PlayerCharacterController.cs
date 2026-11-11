using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>WASD movement within the terminal plaza and entrance walkway.</summary>
public sealed class PlayerCharacterController : MonoBehaviour
{
    [SerializeField, Min(0)] private float moveSpeed = 3f;
    [SerializeField] private Transform visual;
    private Vector3 visualOrigin;
    private float walkTime;

    private void Awake()
    {
        if (visual != null) visualOrigin = visual.localPosition;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;
        if (keyboard != null && Application.isFocused)
        {
            input.x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            input.y = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
        }
        Move(input, Time.deltaTime);
    }

    public void Move(Vector2 input, float deltaTime)
    {
        Vector2 position = transform.position;
        Vector2 step = Vector2.ClampMagnitude(input, 1) * (moveSpeed * Mathf.Max(0, deltaTime));
        // Axis-separated movement slides along the edges of the painted walkway.
        var next = position + new Vector2(step.x, 0);
        if (IsWalkable(next)) position = next;
        next = position + new Vector2(0, step.y);
        if (IsWalkable(next)) position = next;
        transform.position = new Vector3(position.x, position.y, 0);
        if (visual == null) return;
        bool walking = step.sqrMagnitude > 0;
        walkTime = walking ? walkTime + deltaTime * 12 : 0;
        visual.localPosition = visualOrigin + Vector3.up * (walking ? Mathf.Abs(Mathf.Sin(walkTime)) * .045f : 0);
        if (input.x != 0) visual.localScale = new Vector3(Mathf.Sign(input.x), 1, 1);
    }

    public static bool IsWalkable(Vector2 p)
    {
        // Coordinates are feet positions; scenery remains non-interactive.
        return (p.x >= -7.8f && p.x <= 7.8f && p.y >= -1.15f && p.y <= .1f)
            || (p.x >= -3.5f && p.x <= 3.5f && p.y >= -2.6f && p.y <= .1f)
            || (p.x >= -.85f && p.x <= .85f && p.y >= -4.25f && p.y <= -1f)
            || (p.x >= -1.35f && p.x <= 1.35f && p.y >= 0 && p.y <= 3.1f);
    }
}
