using System.Collections.Generic;
using UnityEngine;
using TMPro;
using CNF.Data;
using CNF.AbilitySystem;
using AlmaAscention.Deck;
using AlmaAscention.Combat;

public sealed class SimpleBattleScene : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite backgroundArt;
    [SerializeField] private Sprite playerArt;
    [SerializeField] private CardDataSO[] startingCards;
    public BattleFlow Flow { get; private set; }
    public StatusController Enemy { get; private set; }
    private MonsterDataSO monster;
    private TMP_Text header, playerInfo, enemyInfo, message, piles;
    private UnityEngine.UI.Button endTurn, back;
    private readonly List<UnityEngine.UI.Button> cardButtons = new();
    private readonly List<TMP_Text> cardLabels = new();
    private readonly List<CardInstance> visibleCards = new();
    private RectTransform canvasRoot;
    private string log = "카드를 클릭해 사용하세요. 방어도로 적의 공격을 막을 수 있습니다.";

    private void Start()
    {
        BuildView();
        var player = new GameObject("PlayerStatus").AddComponent<StatusController>(); player.transform.SetParent(transform); player.TeamID=0; player.InitializeStats(50,50,3);
        Enemy = new GameObject("EnemyStatus").AddComponent<StatusController>(); Enemy.transform.SetParent(transform); Enemy.TeamID=1; Enemy.InitializeStats(36,36,0);
        monster=ScriptableObject.CreateInstance<MonsterDataSO>(); monster.MonsterID="DemoGuardian";monster.MonsterName="탑의 수문장";
        monster.Patterns.Add(new MonsterSkillPatternData { PatternID="Strike",PatternName="암흑의 일격",BaseDamage=7,HitCount=1,IntentType=EMonsterIntentType.Attack });
        var deck = new MasterDeck(); foreach(var data in startingCards) if(data!=null) deck.CreateAndAddCardOrNull(data);
        var executor=gameObject.AddComponent<CardExecutor>(); Flow=gameObject.AddComponent<BattleFlow>();
        Flow.Setup(player,deck,new List<(MonsterDataSO,StatusController)>{(monster,Enemy)},executor,173,5);
        Flow.DeckManager.Shuffle();
        Flow.OnLogMessage += OnLog; Flow.StartBattle(); Refresh();
    }
    private void OnLog(string value) => log=value;
    private void Update() { if(Flow!=null) Refresh(); }
    private void OnDestroy() { if(Flow!=null) Flow.OnLogMessage-=OnLog; if(monster!=null) Destroy(monster); }

    public bool PlayCard(int index)
    {
        if(Flow==null || Flow.CardExecutor.IsBusy || index<0 || index>=visibleCards.Count) return false;
        var card=visibleCards[index];
        if(!Flow.CardExecutor.CanPlayCard(card,new CombatContext {Caster=Flow.Player,PrimaryTarget=Enemy},out string reason)) {log=reason;return false;}
        return Flow.TryPlayCard(card,Enemy);
    }
    private void Refresh()
    {
        bool finished=Flow.State==EBattleState.Victory || Flow.State==EBattleState.Defeat;
        header.text=finished ? (Flow.State==EBattleState.Victory?"전투 승리":"전투 패배") : $"1층 · 수문장의 방     /     {Flow.RoundNumber}턴";
        playerInfo.text=$"플레이어\nHP {Flow.Player.CurrentHP} / {Flow.Player.MaxHP}     방어 {Flow.Player.Block}\n마나 {Flow.Player.CurrentMana} / {Flow.Player.MaxMana}";
        var runtime=Flow.Enemies[0];
        enemyInfo.text=$"탑의 수문장\nHP {Enemy.CurrentHP} / {Enemy.MaxHP}     방어 {Enemy.Block}\n"+(Enemy.IsAlive?$"다음 행동 : 공격 {runtime.GetPreviewDamagePerHit()}":"쓰러짐");
        message.text=finished ? (Flow.State==EBattleState.Victory?"수문장을 물리쳤습니다. 터미널로 돌아갈 수 있습니다.":"전투에서 패배했습니다. 터미널에서 다시 도전하세요.") : log;
        piles.text=$"뽑을 카드 {Flow.DeckManager.DrawPile.Count}     버린 카드 {Flow.DeckManager.DiscardPile.Count}     소멸 {Flow.DeckManager.ExhaustPile.Count}";
        visibleCards.Clear();visibleCards.AddRange(Flow.DeckManager.HandCards);
        var ctx=new CombatContext {Caster=Flow.Player,PrimaryTarget=Enemy,AllEnemies=new[]{Enemy},DeckOps=Flow.DeckManager};
        for(int i=0;i<cardButtons.Count;i++) {
            bool show=i<visibleCards.Count;cardButtons[i].gameObject.SetActive(show);if(!show)continue;
            var card=visibleCards[i];cardLabels[i].text=$"<size=27>마나 {card.CurrentCost}</size>\n\n<b>{card.DisplayName}</b>\n\n<size=25>{card.GetDynamicDescription(ctx)}</size>";
            cardButtons[i].interactable=!finished && Flow.State==EBattleState.PlayerTurn && !Flow.CardExecutor.IsBusy && Flow.Player.HasEnoughMana(card.CurrentCost);
        }
        endTurn.interactable=Flow.CanEndTurn;back.gameObject.SetActive(finished);
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
        var go=Box(name,canvasRoot,at,size,new Color(.13f,.10f,.23f,.97f));var image=go.GetComponent<UnityEngine.UI.Image>();image.raycastTarget=true;
        var button=go.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.onClick.AddListener(action);label=Text(go,text,32);return button;
    }
    private void BuildView()
    {
        var go=new GameObject("BattleCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));go.transform.SetParent(transform,false);canvasRoot=(RectTransform)go.transform;
        go.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        var scaler=go.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var bg=Box("Background",canvasRoot,Vector2.zero,new Vector2(1920,1080),new Color(.42f,.38f,.55f));bg.GetComponent<UnityEngine.UI.Image>().sprite=backgroundArt;
        header=Text(Box("Header",canvasRoot,new Vector2(0,455),new Vector2(1750,90),new Color(.035f,.025f,.07f,.9f)),"",38);
        var hero=Box("PlayerPortrait",canvasRoot,new Vector2(-520,100),new Vector2(300,390),Color.white);hero.GetComponent<UnityEngine.UI.Image>().sprite=playerArt;hero.GetComponent<UnityEngine.UI.Image>().preserveAspect=true;
        var enemy=Box("Guardian",canvasRoot,new Vector2(520,100),new Vector2(330,420),new Color(.56f,.34f,.75f));enemy.GetComponent<UnityEngine.UI.Image>().sprite=playerArt;enemy.GetComponent<UnityEngine.UI.Image>().preserveAspect=true;enemy.transform.localScale=new Vector3(-1,1,1);
        playerInfo=Text(Box("PlayerInfo",canvasRoot,new Vector2(-520,-150),new Vector2(570,140),new Color(.035f,.025f,.07f,.9f)),"",30);
        enemyInfo=Text(Box("EnemyInfo",canvasRoot,new Vector2(520,-150),new Vector2(570,140),new Color(.035f,.025f,.07f,.9f)),"",30);
        message=Text(Box("BattleLog",canvasRoot,new Vector2(0,340),new Vector2(1700,75),new Color(.035f,.025f,.07f,.85f)),"",27);
        piles=Text(Box("Piles",canvasRoot,new Vector2(-170,-485),new Vector2(1200,60),Color.clear),"",25);
        for(int i=0;i<5;i++){int slot=i;var b=Button("Card"+i,new Vector2(-640+i*280,-340),new Vector2(258,240),"",()=>PlayCard(slot),out var label);cardButtons.Add(b);cardLabels.Add(label);}
        endTurn=Button("EndTurn",new Vector2(780,-485),new Vector2(270,70),"턴 종료",()=>Flow.TryEndPlayerTurn(),out _);
        back=Button("Return",new Vector2(0,80),new Vector2(380,90),"터미널로 돌아가기",()=>SceneFadeTransition.Go("TerminalScene"),out _);back.gameObject.SetActive(false);
    }
}
