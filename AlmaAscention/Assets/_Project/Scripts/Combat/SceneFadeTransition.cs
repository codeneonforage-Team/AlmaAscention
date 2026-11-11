using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SceneFadeTransition : MonoBehaviour
{
    public static bool IsTransitioning { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => IsTransitioning = false;
    public static void Go(string scene)
    {
        if (IsTransitioning) return;
        if (!Application.CanStreamedLevelBeLoaded(scene)) { Debug.LogError("Scene not registered: " + scene); return; }
        IsTransitioning = true;
        var host = new GameObject("SceneTransition");
        DontDestroyOnLoad(host);
        host.AddComponent<SceneFadeTransition>().StartCoroutine(Fade(scene));
    }
    private static IEnumerator Fade(string scene)
    {
        var host = Object.FindAnyObjectByType<SceneFadeTransition>();
        var canvas = host.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
        host.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        var go = new GameObject("Fade", typeof(RectTransform), typeof(UnityEngine.UI.Image)); go.transform.SetParent(host.transform, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = go.GetComponent<UnityEngine.UI.Image>();
        for (float t=0;t<.35f;t+=Time.unscaledDeltaTime) { image.color = new Color(0,0,0,t/.35f); yield return null; }
        image.color = Color.black;
        yield return SceneManager.LoadSceneAsync(scene);
        for(float t=0;t<.35f;t+=Time.unscaledDeltaTime) { image.color=new Color(0,0,0,1-t/.35f); yield return null; }
        IsTransitioning=false; Destroy(host.gameObject);
    }
}
