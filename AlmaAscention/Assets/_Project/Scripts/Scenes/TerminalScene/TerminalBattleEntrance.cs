using UnityEngine;

public sealed class TerminalBattleEntrance : MonoBehaviour
{
    [SerializeField] private Vector2 entranceCenter = new Vector2(0, 1.25f);
    [SerializeField] private Vector2 entranceSize = new Vector2(2.7f, .6f);
    [SerializeField] private string battleScene = "BattleScene";
    private PlayerCharacterController player;
    private void Start() => player = FindAnyObjectByType<PlayerCharacterController>();
    private void Update()
    {
        if (player == null || SceneFadeTransition.IsTransitioning) return;
        Vector2 d = (Vector2)player.transform.position - entranceCenter;
        if (Mathf.Abs(d.x) <= entranceSize.x/2 && Mathf.Abs(d.y) <= entranceSize.y/2)
        {
            player.enabled=false;
            SceneFadeTransition.Go(battleScene);
        }
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color=Color.cyan; Gizmos.DrawWireCube(entranceCenter, entranceSize);
    }
}
