using System.Collections.Generic;
using UnityEngine;
using TMPro;
using CNF.Data;
using CNF.AbilitySystem;
using AlmaAscention.Combat;

public sealed class BattleScene : MonoBehaviour
{
    [SerializeField] private Sprite enemyArt;
    [SerializeField] private UnityEngine.UI.Image enemyPortrait;
    [SerializeField] private CardDataSO[] startingCards;
    public BattleFlow Flow { get; private set; }
    public StatusController Enemy { get; private set; }
    private MonsterDataSO monster;
    [SerializeField] private TMP_Text header;
    [SerializeField] private TMP_Text playerInfo;
    [SerializeField] private TMP_Text enemyInfo;
    [SerializeField] private TMP_Text message;
    [SerializeField] private TMP_Text piles;
    [SerializeField] private UnityEngine.UI.Button endTurn;
    private UnityEngine.UI.Button regainOneTurn;
    private UnityEngine.UI.Button regainTwoTurns;
    [SerializeField] private UnityEngine.UI.Button skipBattle;
    [SerializeField] private CardHandUI cardUI;
    [SerializeField] private AscentRouteUI route;
    private AscentRun run;
    private readonly System.Random random = new();
    private GameObject encounterRoot;
    [SerializeField] private RectTransform battleHud;
    private readonly List<CardInstance> visibleCards = new();
    private string log = "손패의 카드를 위쪽 전투 영역으로 드래그해 사용하세요.";

    private void Start()
    {
        cardUI.Initialize(this);
        endTurn.onClick.AddListener(() =>
        {
            if (Flow == null) return;
            if (Flow.IsDefeatPending) Flow.ConfirmDefeat();
            else Flow.TryEndPlayerTurn();
        });
        regainOneTurn = CreateRegainButton("RegainOneTurn", 95, 1);
        regainTwoTurns = CreateRegainButton("RegainTwoTurns", 180, 2);
        skipBattle.gameObject.SetActive(Debug.isDebugBuild);
        skipBattle.onClick.AddListener(() => Flow?.SkipBattleForDevelopment());
        run = new AscentRun(startingCards, random);
        ShowBattle(false);
        route.Initialize(run, StartEncounter);
    }

    private UnityEngine.UI.Button CreateRegainButton(string name, float yOffset, int turns)
    {
        var button = Instantiate(endTurn, endTurn.transform.parent);
        button.name = name;
        var rect = (RectTransform)button.transform;
        rect.anchoredPosition = ((RectTransform)endTurn.transform).anchoredPosition + Vector2.up * yOffset;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => Flow?.TryRegain(turns));
        var label = button.GetComponentInChildren<TMP_Text>();
        label.fontSize = 24;
        label.text = $"재회 · {turns}턴";
        return button;
    }

    private void StartEncounter(MonsterDataSO encounter)
    {
        encounterRoot = new GameObject("Encounter");
        encounterRoot.transform.SetParent(transform);
        var player = NewCombatant("PlayerStatus", 0);
        player.InitializeStats(run.HP, run.MaxHP, 3);
        monster = encounter;
        enemyPortrait.sprite = monster.PortraitOrSprite != null ? monster.PortraitOrSprite : enemyArt;
        enemyPortrait.enabled = enemyPortrait.sprite != null;
        Enemy = NewCombatant(monster.MonsterName, 1);
        Enemy.InitializeFromMonsterData(monster, random);
        var executor = encounterRoot.AddComponent<CardExecutor>();
        Flow = encounterRoot.AddComponent<BattleFlow>();
        Flow.Setup(player, run.Deck, new List<(MonsterDataSO, StatusController)> { (monster, Enemy) },
            executor, random.Next(), 5, run.Relics, run.Potions,
            () => run.RemainingChapterRegains, run.SpendChapterRegain);
        Flow.OnLogMessage += OnLog;
        Flow.OnBattleEnded += FinishEncounter;
        ShowBattle(true);
        Flow.StartBattle();
        Refresh();
    }

    private StatusController NewCombatant(string objectName, int team)
    {
        var status = new GameObject(objectName).AddComponent<StatusController>();
        status.transform.SetParent(encounterRoot.transform);
        status.TeamID = team;
        return status;
    }

    private void FinishEncounter(bool won)
    {
        string outcome = won ? $"{monster.MonsterName} 격파 · 골드 +{run.Current.VictoryGold}" : "전투에서 패배했습니다.";
        run.FinishBattle(won, Flow.Player.CurrentHP, Flow.Player.MaxHP);
        Flow.OnBattleEnded -= FinishEncounter;
        Flow.OnLogMessage -= OnLog;
        Flow = null;
        Enemy = null;
        Destroy(encounterRoot);
        monster = null;
        ShowBattle(false);
        if (run.RewardPending) route.ShowReward(outcome);
        else route.ShowProgress(outcome);
    }

    private void ShowBattle(bool visible)
    {
        battleHud.gameObject.SetActive(visible);
        cardUI.gameObject.SetActive(visible);
    }
    private void OnLog(string value) => log=value;
    private void Update() { if(Flow!=null) Refresh(); }
    private void OnDestroy() { if(Flow!=null) Flow.OnLogMessage-=OnLog; }

    public bool PlayCard(int index)
    {
        if(Flow==null || Flow.CardExecutor.IsBusy || index<0 || index>=visibleCards.Count) return false;
        return PlayCard(visibleCards[index]);
    }
    public bool PlayCard(CardInstance card)
    {
        if (cardUI == null || !cardUI.CanUse(card)) return false;
        if(!Flow.CardExecutor.CanPlayCard(card,new CombatContext {Caster=Flow.Player,PrimaryTarget=Enemy},out string reason)) {log=reason;return false;}
        return Flow.TryPlayCard(card,Enemy);
    }
    private void Refresh()
    {
        string type = run.Current.Type == RouteNodeType.Boss ? "보스 전투" : "일반 전투";
        header.text=$"스테이지 {run.Stage} · {run.Step}단계     /     {type}     /     {Flow.RoundNumber}턴";
        playerInfo.text=$"플레이어\nHP {Flow.Player.CurrentHP} / {Flow.Player.MaxHP}     방어 {Flow.Player.Block}\n마나 {Flow.Player.CurrentMana} / {Flow.Player.MaxMana}";
        var runtime=Flow.Enemies[0];
        string intent = runtime.CurrentIntent?.PatternName ?? "행동 없음";
        if (runtime.GetPreviewDamagePerHit() > 0)
        {
            intent += $" · 공격 {runtime.GetPreviewDamagePerHit()}";
            if (runtime.GetPreviewHitCount() > 1) intent += $" × {runtime.GetPreviewHitCount()}";
        }
        if (runtime.GetPreviewBlock() > 0) intent += $" · 방어 +{runtime.GetPreviewBlock()}";
        string phase = runtime.CurrentPhase?.PhaseName;
        enemyInfo.text=$"{monster.MonsterName}" +
            (string.IsNullOrEmpty(phase) || phase == monster.MonsterName ? "" : $" · {phase}") +
            $"\nHP {Enemy.CurrentHP} / {Enemy.MaxHP}     방어 {Enemy.Block}\n다음 행동 · {intent}";
        message.text=log;
        piles.text=$"뽑을 카드 {Flow.DeckManager.DrawPile.Count}     버린 카드 {Flow.DeckManager.DiscardPile.Count}     소멸 {Flow.DeckManager.ExhaustPile.Count}";
        visibleCards.Clear();visibleCards.AddRange(Flow.DeckManager.HandCards);
        var ctx=new CombatContext {Caster=Flow.Player,PrimaryTarget=Enemy,AllEnemies=new[]{Enemy},DeckOps=Flow.DeckManager};
        cardUI.Refresh(ctx);
        endTurn.GetComponentInChildren<TMP_Text>().text = Flow.IsDefeatPending ? "패배 확정" : "턴 종료";
        endTurn.interactable=(Flow.CanEndTurn && !cardUI.IsDragging) || Flow.IsDefeatPending;
        regainOneTurn.interactable=Flow.CanRegain(1) && !cardUI.IsDragging;
        regainTwoTurns.interactable=Flow.CanRegain(2) && !cardUI.IsDragging;
        regainOneTurn.GetComponentInChildren<TMP_Text>().text =
            $"재회 현재 턴 · 전투{Flow.RemainingBattleRegains} 장{Flow.RemainingChapterRegains}";
        regainTwoTurns.GetComponentInChildren<TMP_Text>().text = "재회 이전 턴";
    }
}
