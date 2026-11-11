using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Edits the title menu appearance and exposes its button actions.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class TitleSceneUI : MonoBehaviour
{
    [System.Serializable]
    public sealed class MenuButton
    {
        [SerializeField] private UnityEngine.UI.Button button;
        [SerializeField] private TMP_Text label;
        [SerializeField] private string text;
        [SerializeField] private Color backgroundColor;
        [SerializeField] private Color textColor;
        [SerializeField] private bool interactable = true;
        [SerializeField] private UnityEvent onClick = new UnityEvent();

        public MenuButton(string text, Color backgroundColor, Color textColor)
        {
            this.text = text;
            this.backgroundColor = backgroundColor;
            this.textColor = textColor;
        }

        public void Apply(float fontSize, Vector2 size)
        {
            if (label != null)
            {
                label.text = text;
                label.fontSize = fontSize;
                label.color = textColor;
            }

            if (button == null) return;
            button.interactable = interactable;
            if (button.targetGraphic != null) button.targetGraphic.color = backgroundColor;
            var layout = button.GetComponent<UnityEngine.UI.LayoutElement>();
            if (layout != null)
            {
                layout.preferredWidth = size.x;
                layout.preferredHeight = size.y;
            }
        }

        public void Bind()
        {
            if (button == null) return;
            button.onClick.RemoveListener(Invoke);
            button.onClick.AddListener(Invoke);
        }

        public void Unbind()
        {
            if (button != null) button.onClick.RemoveListener(Invoke);
        }

        private void Invoke() => onClick?.Invoke();
    }

    [Header("UI References")]
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private UnityEngine.UI.Image background;
    [SerializeField] private UnityEngine.UI.VerticalLayoutGroup menuLayout;

    [Header("Title")]
    [SerializeField] private string title = "ALMA ASCENTION";
    [SerializeField, Min(1)] private float titleFontSize = 52;
    [SerializeField] private Color titleColor = new Color(.9f, .83f, .62f, 1);

    [Header("Background")]
    [SerializeField] private bool showBackground = true;
    [SerializeField] private Color backgroundColor = new Color(.035f, .055f, .1f, 1);

    [Header("Button Layout")]
    [SerializeField] private Vector2 buttonSize = new Vector2(420, 76);
    [SerializeField, Min(1)] private float buttonFontSize = 30;
    [SerializeField, Min(0)] private float buttonSpacing = 18;

    [Header("Scene Navigation")]
    [SerializeField] private string gameSceneName = "TerminalScene";

    [Header("Buttons / Click Events")]
    [SerializeField] private MenuButton startButton = new MenuButton("게임시작", new Color(.76f, .64f, .38f, 1), new Color(.055f, .07f, .1f, 1));
    [SerializeField] private MenuButton continueButton = new MenuButton("이어하기", new Color(.12f, .17f, .25f, 1), new Color(.94f, .96f, 1, 1));
    [SerializeField] private MenuButton settingsButton = new MenuButton("설정", new Color(.12f, .17f, .25f, 1), new Color(.94f, .96f, 1, 1));
    [SerializeField] private MenuButton quitButton = new MenuButton("종료", new Color(.12f, .17f, .25f, 1), new Color(.94f, .96f, 1, 1));

    private void Awake() => ApplyAppearance();

    private void OnEnable()
    {
        startButton?.Bind();
        continueButton?.Bind();
        settingsButton?.Bind();
        quitButton?.Bind();
    }

    private void OnDisable()
    {
        startButton?.Unbind();
        continueButton?.Unbind();
        settingsButton?.Unbind();
        quitButton?.Unbind();
    }

    public void StartGame()
    {
        if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            Debug.LogError($"Cannot load scene '{gameSceneName}'. Enable it in the build scene list.", this);
            return;
        }

        UnityEngine.SceneManagement.SceneManager.LoadScene(gameSceneName);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    [ContextMenu("Apply Appearance")]
    public void ApplyAppearance()
    {
        if (titleLabel != null)
        {
            titleLabel.text = title;
            titleLabel.fontSize = titleFontSize;
            titleLabel.color = titleColor;
        }
        if (background != null)
        {
            background.enabled = showBackground;
            background.color = backgroundColor;
        }
        if (menuLayout != null) menuLayout.spacing = buttonSpacing;
        var size = new Vector2(Mathf.Max(1, buttonSize.x), Mathf.Max(1, buttonSize.y));
        startButton?.Apply(buttonFontSize, size);
        continueButton?.Apply(buttonFontSize, size);
        settingsButton?.Apply(buttonFontSize, size);
        quitButton?.Apply(buttonFontSize, size);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // TMP and layout updates must run after Unity finishes serialization.
        UnityEditor.EditorApplication.delayCall -= RefreshPreview;
        UnityEditor.EditorApplication.delayCall += RefreshPreview;
    }

    private void RefreshPreview()
    {
        if (this != null && !UnityEditor.EditorApplication.isCompiling)
            ApplyAppearance();
    }
#endif
}
