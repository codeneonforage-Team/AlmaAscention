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

    private static string Describe(RouteNode node)
    {
        switch (node.Type)
        {
            case RouteNodeType.Rest:
                return $"휴식 구역\n체력 +{node.RestHealing}" + (node.RestGold > 0 ? $" · 골드 +{node.RestGold}" : "") + " 또는 카드 강화";
            case RouteNodeType.Shop:
                return $"상점\n랜덤 카드 진열 · {AscentRun.CardPrice} 골드~";
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
                ShowRestSite();
                break;
            case RouteNodeType.Shop:
                ShowShop();
                break;
            default:
                gameObject.SetActive(false);
                onBattleSelected();
                break;
        }
    }

    private void ShowRestSite()
    {
        Show("휴식 구역", "체력을 회복하거나 보유한 카드를 1장 영구 강화할 수 있습니다.");
        string healText = $"휴식하기\n체력 +{run.RestSite.PlannedHealAmount}" +
                          (run.RestSite.PlannedBonusGold > 0 ? $" · 골드 +{run.RestSite.PlannedBonusGold}" : "");
        ActionButton(0, healText, () =>
        {
            int before = run.HP;
            int gold = run.RestSite.PlannedBonusGold;
            run.Rest();
            ShowProgress($"휴식 완료 · 체력 +{run.HP - before}" + (gold > 0 ? $" · 골드 +{gold}" : ""));
        });

        bool canSmith = run.RestSite.CanSmith(run.Deck);
        ActionButton(1, "단련하기 (카드 강화+)\n미강화 카드 1장 선택", ShowRestSmithSelection, canSmith);
    }

    private void ShowRestSmithSelection()
    {
        var upgradeable = run.RestSite.GetUpgradeableCards(run.Deck, distinctByCardId: true);
        Show("단련할 카드를 선택하세요", "선택한 카드가 영구적으로 강화(+)됩니다.");

        int maxChoices = Mathf.Min(2, upgradeable.Count);
        for (int i = 0; i < maxChoices; i++)
        {
            CardInstance candidate = upgradeable[i];
            string upgDesc = candidate.Data != null ? candidate.Data.GetFormattedBaseDescription(isUpgraded: true) : "";
            ActionButton(i, $"강화 · {candidate.DisplayName} -> {candidate.DisplayName}+\n{upgDesc}", () =>
            {
                if (run.TrySmithAtRestSite(candidate.InstanceGUID, out var upgraded))
                    ShowProgress($"단련 완료 · [{upgraded.DisplayName}] 강화!");
            });
        }

        ActionButton(2, "돌아가기", ShowRestSite);
    }

    private void ShowShop(string notice = "")
    {
        var offers = run.Shop.CardOffers;
        string detail = string.IsNullOrEmpty(notice)
            ? "원하는 카드를 구매하세요. 골드가 충분하면 여러 장 구매할 수 있습니다."
            : notice;
        Show("카드 상점", detail);

        int maxSlots = Mathf.Min(2, offers.Count);
        for (int i = 0; i < maxSlots; i++)
        {
            int slotIndex = i;
            var offer = offers[i];
            if (offer.IsSold || offer.CardData == null)
            {
                ActionButton(i, "구매 완료", () => { }, enabled: false);
            }
            else
            {
                string caption = $"구매 · {offer.CardData.CardName} ({offer.Price} 골드)\n{offer.CardData.GetFormattedBaseDescription()}";
                ActionButton(i, caption, () =>
                {
                    if (run.TryBuyShopCardOffer(slotIndex, out var bought))
                        ShowShop($"{bought.DisplayName} 카드를 덱에 추가했습니다.");
                }, run.Shop.CanBuyCard(slotIndex, run.Gold));
            }
        }

        ActionButton(2, "상점 나가기", () =>
        {
            if (run.ExitShop())
                ShowProgress();
        });
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
