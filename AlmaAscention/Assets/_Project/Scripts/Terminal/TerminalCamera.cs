using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class TerminalCamera : MonoBehaviour
{
    private Camera view;
    private void Awake() => view = GetComponent<Camera>();
    private void LateUpdate()
    {
        // Keep the complete 20 x 10 painted environment visible at any aspect ratio.
        view.orthographicSize = Mathf.Max(5f, 10f / Mathf.Max(.1f, view.aspect));
    }
}
