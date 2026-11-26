using System;
using System.Collections.Generic;
using AlmaAscention.Deck;
using AlmaAscention.Relic;
using AlmaAscention.Potion;
using AlmaAscention.Shop;
using AlmaAscention.Rest;
using CNF.Data;

public enum RouteNodeType { Battle, Rest, Shop, Boss }
public enum AscentState { Choosing, InNode, Cleared, Defeated }

public readonly struct RouteNode
{
    public readonly RouteNodeType Type;
    public readonly int Variant;

    public RouteNode(RouteNodeType type, int variant)
    {
        Type = type;
        Variant = variant;
    }

    public int RestHealing => 15 - 4 * Variant;
    public int RestGold => 5 * Variant;
    public int VictoryGold => Type == RouteNodeType.Boss ? 25 : 12 + 2 * Variant;
}

// One climb owns the permanent deck and HP. BattleFlow gets a fresh combat deck per battle.
public sealed class AscentRun
{
    public const int StageCount = 5;
    public const int StepsPerStage = 10;
    public const int CardPrice = 15;
    public const int RegainsPerChapter = 3;
    public const int RerollsPerBattle = 2;
    private readonly Random random;
    private readonly RouteNode[] choices = new RouteNode[3];

    public int Stage { get; private set; } = 1;
    public int Step { get; private set; } = 1;
    public int HP { get; private set; } = 50;
    public int MaxHP { get; private set; } = 50;
    public int Gold { get; private set; } = 15;
    public MasterDeck Deck { get; } = new();
    public RelicManager Relics { get; } = new();
    public PotionManager Potions { get; } = new();
    public ShopManager Shop { get; } = new();
    public RestSiteManager RestSite { get; } = new();
    public IReadOnlyList<RouteNode> Choices { get; }
    public RouteNode Current { get; private set; }
    public AscentState State { get; private set; }
    public int RemainingChapterRegains { get; private set; } = RegainsPerChapter;
    public int RemainingRewardRerolls { get; private set; }
    public bool RewardPending { get; private set; }
    private readonly List<CardDataSO> rewardCards = new();
    public IReadOnlyList<CardDataSO> RewardCards => rewardCards;

    public void SpendChapterRegain()
    {
        if (RemainingChapterRegains <= 0) throw new InvalidOperationException("재회 사용 횟수가 없습니다.");
        RemainingChapterRegains--;
    }

    public AscentRun(IEnumerable<CardDataSO> startingCards, Random random)
    {
        this.random = random;
        Choices = Array.AsReadOnly(choices);
        if (startingCards != null)
        {
            foreach (var card in startingCards)
                if (card != null) Deck.CreateAndAddCardOrNull(card);
        }
        if (Deck.Count == 0 && DataManager.Instance != null)
        {
            foreach (var card in DataManager.Instance.GetDefaultStarterCards())
                if (card != null) Deck.CreateAndAddCardOrNull(card);
        }
        RollChoices();
    }

    private void RollChoices()
    {
        // Each slot is an independent roll. Shuffle the three encounter variants among the slots.
        int offset = random.Next(3);
        int stride = random.Next(2) + 1;
        int battlePercent = 80 - (Step - 1) * 7;
        for (int i = 0; i < choices.Length; i++)
        {
            RouteNodeType type = RouteNodeType.Boss;
            if (Step < StepsPerStage)
            {
                int roll = random.Next(200);
                type = roll < 2 * battlePercent ? RouteNodeType.Battle
                    : roll < 100 + battlePercent ? RouteNodeType.Rest : RouteNodeType.Shop;
            }
            choices[i] = new RouteNode(type, (offset + i * stride) % 3);
        }
        State = AscentState.Choosing;
    }

    public bool TryChoose(int index)
    {
        if (State != AscentState.Choosing || index < 0 || index >= choices.Length) return false;
        Current = choices[index];
        State = AscentState.InNode;
        RewardPending = false;
        rewardCards.Clear();
        RemainingRewardRerolls = RerollsPerBattle;

        if (Current.Type == RouteNodeType.Rest)
            RestSite.EnterRestSite(MaxHP, Current.RestHealing, Current.RestGold);
        else if (Current.Type == RouteNodeType.Shop)
            Shop.OpenShop(Relics, cardOfferCount: 2, baseCardPrice: CardPrice);

        return true;
    }

    public void FinishBattle(bool won, int hp, int maxHP)
    {
        if (State != AscentState.InNode ||
            (Current.Type != RouteNodeType.Battle && Current.Type != RouteNodeType.Boss)) return;
        MaxHP = Math.Max(1, maxHP);
        HP = Math.Clamp(hp, 0, MaxHP);
        if (!won || HP == 0) { State = AscentState.Defeated; return; }
        Gold += Current.VictoryGold;
        if (DataManager.Instance != null)
            rewardCards.AddRange(DataManager.Instance.GetRandomCards(2));
        RewardPending = rewardCards.Count > 0;
        if (!RewardPending) Advance();
    }

    public bool TryRerollReward()
    {
        if (!RewardPending || RemainingRewardRerolls <= 0 || DataManager.Instance == null) return false;
        var excluded = new List<string>();
        foreach (var card in rewardCards) excluded.Add(card.CardID);
        var next = DataManager.Instance.GetRandomCards(rewardCards.Count, excludeCardIds: excluded);
        if (next.Count == 0) return false;
        rewardCards.Clear();
        rewardCards.AddRange(next);
        RemainingRewardRerolls--;
        return true;
    }

    public bool TryTakeReward(int index, out CardInstance card)
    {
        card = null;
        if (!RewardPending || index < 0 || index >= rewardCards.Count) return false;
        card = Deck.CreateAndAddCardOrNull(rewardCards[index]);
        if (card == null) return false;
        RewardPending = false;
        rewardCards.Clear();
        Advance();
        return true;
    }

    public bool SkipReward()
    {
        if (!RewardPending) return false;
        RewardPending = false;
        rewardCards.Clear();
        Advance();
        return true;
    }

    public void Rest()
    {
        if (State != AscentState.InNode || Current.Type != RouteNodeType.Rest) return;
        int hp = HP;
        int gold = Gold;
        if (RestSite.TryRest(ref hp, MaxHP, ref gold, out _))
        {
            HP = hp;
            Gold = gold;
            Advance();
        }
    }

    public bool TrySmithAtRestSite(string cardInstanceGuid, out CardInstance upgradedCard)
    {
        upgradedCard = null;
        if (State != AscentState.InNode || Current.Type != RouteNodeType.Rest) return false;
        if (!RestSite.TrySmithCard(Deck, cardInstanceGuid, out upgradedCard)) return false;
        Advance();
        return true;
    }

    public bool TryBuyShopCardOffer(int offerIndex, out CardInstance purchasedCard)
    {
        purchasedCard = null;
        if (State != AscentState.InNode || Current.Type != RouteNodeType.Shop) return false;
        int gold = Gold;
        if (!Shop.TryBuyCard(offerIndex, Deck, ref gold, out purchasedCard)) return false;
        Gold = gold;
        return true;
    }

    public bool ExitShop()
    {
        if (State != AscentState.InNode || Current.Type != RouteNodeType.Shop) return false;
        Advance();
        return true;
    }

    private void Advance()
    {
        if (Step == StepsPerStage)
        {
            if (Stage == StageCount) { State = AscentState.Cleared; return; }
            Stage++;
            Step = 1;
            RemainingChapterRegains = RegainsPerChapter;
        }
        else Step++;
        RollChoices();
    }
}
