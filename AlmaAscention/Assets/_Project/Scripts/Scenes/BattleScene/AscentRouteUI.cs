using System;
using CNF.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Button = UnityEngine.UI.Button;

public sealed class AscentRouteUI : MonoBehaviour
{
    [SerializeField] private TMP_Text location;
    [SerializeField] private TMP_Text heading;
    [SerializeField] private TMP_Text resources;
    [SerializeField] private Button[] buttons;
    [SerializeField] private TMP_Text[] captions;

    private static readonly string[] Stock = { "IronWave", "TwinStrike", "Inflame" };
    private AscentRun run;
    private Action onBattleSelected;

    public void Initialize(AscentRun ascent, Action startBattle)
    {
        run = ascent;
        onBattleSelected = startBattle;
        ShowProgress();
    }

    public void ShowProgress(string outcome = "")
    {
        if (run.State == AscentState.Cleared || run.State == AscentState.Defeated)
        {
            Show(run.State == AscentState.Cleared ? "탑 정상에 도달했습니다" : "등반이 끝났습니다", outcome);
            ActionButton(0, "터미널로 돌아가기", () => SceneFadeTransition.Go("TerminalScene"));
            return;
        }
        Show(run.Step == AscentRun.StepsPerStage ? "도전할 보스를 선택하세요" : "다음 경로를 선택하세요", outcome);
        for (int i = 0; i < run.Choices.Count; i++)
        {
            int index = i;
            var node = run.Choices[i];
            ActionButton(i, Describe(node), () => Choose(index));
        }
    }

    private static CardDataSO ShopCard(RouteNode node) => DataManager.Instance.GetCard(Stock[node.Variant]);

    private static string Describe(RouteNode node)
    {
        switch (node.Type)
        {
            case RouteNodeType.Rest:
                return $"휴식\n체력 +{node.RestHealing}" + (node.RestGold > 0 ? $" · 골드 +{node.RestGold}" : "");
            case RouteNodeType.Shop:
                return $"상점\n{ShopCard(node)?.CardName ?? "품절"} · {AscentRun.CardPrice} 골드";
            default:
                string type = node.Type == RouteNodeType.Boss ? "보스 전투" : "일반 전투";
                return $"{type}\n{RouteEncounter.Preview(node)}\n승리 보상 {node.VictoryGold} 골드";
        }
    }

    private void Choose(int index)
    {
        if (!run.TryChoose(index)) return;
        switch (run.Current.Type)
        {
            case RouteNodeType.Rest:
                Show("휴식", "쉬고 나면 다음 경로를 선택할 수 있습니다.");
                ActionButton(0, Describe(run.Current), () =>
                {
                    int before = run.HP;
                    int gold = run.Current.RestGold;
                    run.Rest();
                    ShowProgress($"휴식 완료 · 체력 +{run.HP - before} · 골드 +{gold}");
                });
                break;
            case RouteNodeType.Shop:
                var card = ShopCard(run.Current);
                Show("카드 상점", card != null ? card.Description : "상품이 소진되었습니다.");
                ActionButton(0, $"구매 · {card?.CardName ?? "품절"}\n{AscentRun.CardPrice} 골드",
                    () => { if (run.LeaveShop(card)) ShowProgress($"{card.CardName} 카드를 덱에 추가했습니다."); },
                    card != null && run.Gold >= AscentRun.CardPrice);
                ActionButton(1, "구매하지 않고 출발", () => { if (run.LeaveShop(null)) ShowProgress(); });
                break;
            default:
                gameObject.SetActive(false);
                onBattleSelected();
                break;
        }
    }

    private void Show(string title, string detail)
    {
        gameObject.SetActive(true);
        location.text = $"스테이지 {run.Stage} / {AscentRun.StageCount}     ·     {run.Step} / {AscentRun.StepsPerStage} 단계";
        heading.text = title;
        resources.text = $"체력 {run.HP} / {run.MaxHP}     ·     골드 {run.Gold}     ·     덱 {run.Deck.Count}장\n{detail}";
        foreach (var button in buttons)
        {
            button.onClick.RemoveAllListeners();
            button.gameObject.SetActive(false);
        }
    }

    private void ActionButton(int index, string caption, UnityAction action, bool enabled = true)
    {
        var button = buttons[index];
        captions[index].text = caption;
        button.interactable = enabled;
        button.onClick.AddListener(action);
        button.gameObject.SetActive(true);
    }
}
