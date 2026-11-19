using System.Collections.Generic;
using UnityEngine;
using TMPro;
using CNF.Data;
using CNF.AbilitySystem;
using AlmaAscention.Combat;

public sealed class SimpleBattleScene : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite backgroundArt;
    [SerializeField] private Sprite playerArt;
    [SerializeField] private Sprite enemyArt;
    [SerializeField] private CardDataSO[] startingCards;
    public BattleFlow Flow { get; private set; }
    public StatusController Enemy { get; private set; }
    private MonsterDataSO monster;
    private TMP_Text header, playerInfo, enemyInfo, message, piles;
    private UnityEngine.UI.Button endTurn;
    [SerializeField] private CardHandUI cardUI;
    [SerializeField] private AscentRouteUI routePrefab;
    private AscentRouteUI route;
    private AscentRun run;
    private readonly System.Random random = new();
    private GameObject encounterRoot;
    private RectTransform battleHud;
    private readonly List<CardInstance> visibleCards = new();
    private RectTransform canvasRoot;
    private string log = "손패의 카드를 위쪽 전투 영역으로 드래그해 사용하세요.";

    private void Start()
    {
        BuildView();
        run = new AscentRun(startingCards, random);
        route = Instantiate(routePrefab, canvasRoot);
        ShowBattle(false);
        route.Initialize(run, StartEncounter);
    }

    private void StartEncounter()
    {
        encounterRoot = new GameObject("Encounter");
        encounterRoot.transform.SetParent(transform);
        var player = NewCombatant("PlayerStatus", 0);
        player.InitializeStats(run.HP, run.MaxHP, 3);
        monster = RouteEncounter.Create(run.Current, run.Stage);
        Enemy = NewCombatant(monster.MonsterName, 1);
        Enemy.InitializeFromMonsterData(monster, random);
        var executor = encounterRoot.AddComponent<CardExecutor>();
        Flow = encounterRoot.AddComponent<BattleFlow>();
        Flow.Setup(player, run.Deck, new List<(MonsterDataSO, StatusController)> { (monster, Enemy) },
            executor, random.Next(), 5);
        Flow.DeckManager.Shuffle();
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
        Destroy(monster);
        monster = null;
        ShowBattle(false);
        route.ShowProgress(outcome);
    }

    private void ShowBattle(bool visible)
    {
        battleHud.gameObject.SetActive(visible);
        cardUI.gameObject.SetActive(visible);
    }
    private void OnLog(string value) => log=value;
    private void Update() { if(Flow!=null) Refresh(); }
    private void OnDestroy() { if(Flow!=null) Flow.OnLogMessage-=OnLog; if(monster!=null) Destroy(monster); }

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
        string intent = $"공격 {runtime.GetPreviewDamagePerHit()}";
        if (runtime.GetPreviewHitCount() > 1) intent += $" × {runtime.GetPreviewHitCount()}";
        if (runtime.GetPreviewBlock() > 0) intent += $" · 방어 +{runtime.GetPreviewBlock()}";
        enemyInfo.text=$"{monster.MonsterName}\nHP {Enemy.CurrentHP} / {Enemy.MaxHP}     방어 {Enemy.Block}\n다음 행동 · {intent}";
        message.text=log;
        piles.text=$"뽑을 카드 {Flow.DeckManager.DrawPile.Count}     버린 카드 {Flow.DeckManager.DiscardPile.Count}     소멸 {Flow.DeckManager.ExhaustPile.Count}";
        visibleCards.Clear();visibleCards.AddRange(Flow.DeckManager.HandCards);
        var ctx=new CombatContext {Caster=Flow.Player,PrimaryTarget=Enemy,AllEnemies=new[]{Enemy},DeckOps=Flow.DeckManager};
        cardUI.Refresh(ctx);
        endTurn.interactable=Flow.CanEndTurn && !cardUI.IsDragging;
    }
    private GameObject Box(string name, Transform parent, Vector2 center, Vector2 size, Color color)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image));go.layer=5;go.transform.SetParent(parent,false);
        var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=center;rect.sizeDelta=size;
        var image=go.GetComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=false;return go;
    }
    private TMP_Text Text(GameObject box,string text,int size)
    {
        var go=new GameObject("Label",typeof(RectTransform));go.layer=5;go.transform.SetParent(box.transform,false);var rect=(RectTransform)go.transform;
        rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(14,10);rect.offsetMax=new Vector2(-14,-10);
        var label=go.AddComponent<TextMeshProUGUI>();label.font=font;label.text=text;label.fontSize=size;label.alignment=TextAlignmentOptions.Center;label.color=new Color(.95f,.92f,1);label.raycastTarget=false;return label;
    }
    private UnityEngine.UI.Button Button(string name,Vector2 at,Vector2 size,string text,UnityEngine.Events.UnityAction action,out TMP_Text label)
    {
        var go=Box(name,battleHud,at,size,new Color(.13f,.10f,.23f,.97f));var image=go.GetComponent<UnityEngine.UI.Image>();image.raycastTarget=true;
        var button=go.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.onClick.AddListener(action);label=Text(go,text,32);return button;
    }
    private void BuildView()
    {
        var go=new GameObject("BattleCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));go.transform.SetParent(transform,false);canvasRoot=(RectTransform)go.transform;
        go.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        var scaler=go.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var bg=Box("Background",canvasRoot,Vector2.zero,new Vector2(1920,1080),new Color(.42f,.38f,.55f));bg.GetComponent<UnityEngine.UI.Image>().sprite=backgroundArt;
        battleHud = (RectTransform)new GameObject("BattleHUD", typeof(RectTransform)).transform;
        battleHud.SetParent(canvasRoot, false);
        battleHud.anchorMin = Vector2.zero;
        battleHud.anchorMax = Vector2.one;
        battleHud.offsetMin = battleHud.offsetMax = Vector2.zero;
        header=Text(Box("Header",battleHud,new Vector2(0,455),new Vector2(1750,90),new Color(.035f,.025f,.07f,.9f)),"",38);
        var hero=Box("PlayerPortrait",battleHud,new Vector2(-520,100),new Vector2(300,390),Color.white);hero.GetComponent<UnityEngine.UI.Image>().sprite=playerArt;hero.GetComponent<UnityEngine.UI.Image>().preserveAspect=true;
        var enemy=Box("Guardian",battleHud,new Vector2(520,100),new Vector2(330,420),new Color(.56f,.34f,.75f));enemy.GetComponent<UnityEngine.UI.Image>().sprite=enemyArt;enemy.GetComponent<UnityEngine.UI.Image>().preserveAspect=true;enemy.transform.localScale=new Vector3(-1,1,1);
        playerInfo=Text(Box("PlayerInfo",battleHud,new Vector2(-520,-150),new Vector2(570,140),new Color(.035f,.025f,.07f,.9f)),"",30);
        enemyInfo=Text(Box("EnemyInfo",battleHud,new Vector2(520,-150),new Vector2(570,140),new Color(.035f,.025f,.07f,.9f)),"",30);
        message=Text(Box("BattleLog",battleHud,new Vector2(0,340),new Vector2(1700,75),new Color(.035f,.025f,.07f,.85f)),"",27);
        piles=Text(Box("Piles",battleHud,new Vector2(0,260),new Vector2(1200,60),Color.clear),"",25);
        cardUI.Initialize(this);
        endTurn=Button("EndTurn",new Vector2(780,-485),new Vector2(270,70),"턴 종료",()=>Flow.TryEndPlayerTurn(),out _);
    }
}
