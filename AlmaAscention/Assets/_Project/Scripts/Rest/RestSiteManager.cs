using System;
using System.Collections.Generic;
using AlmaAscention.Deck;
using CNF.Data;

namespace AlmaAscention.Rest
{
    /// <summary>
    /// 휴식 구역(Rest Site)에서 체력 회복(휴식)과 영구 덱 카드 강화(단련)를 전담하는 매니저.
    /// </summary>
    public sealed class RestSiteManager
    {
        public const float DefaultHealRatio = 0.30f;

        public bool IsActionCompleted { get; private set; }
        public int PlannedHealAmount { get; private set; }
        public int PlannedBonusGold { get; private set; }

        public event Action Changed;

        /// <summary>
        /// 휴식 구역에 진입할 때 회복량과 행동 가능 상태를 초기화합니다.
        /// </summary>
        public void EnterRestSite(int maxHP, int flatHealOverride = 0, int bonusGold = 0)
        {
            IsActionCompleted = false;
            PlannedHealAmount = flatHealOverride > 0
                ? flatHealOverride
                : Math.Max(1, (int)Math.Round(Math.Max(1, maxHP) * DefaultHealRatio));
            PlannedBonusGold = Math.Max(0, bonusGold);
            Changed?.Invoke();
        }

        /// <summary>
        /// [휴식 선택지]: 체력을 회복하고 보너스 골드를 획득합니다.
        /// </summary>
        public bool TryRest(ref int currentHP, int maxHP, ref int gold, out int actualHealed)
        {
            actualHealed = 0;
            if (IsActionCompleted)
                return false;

            int clampedMax = Math.Max(1, maxHP);
            int before = Math.Clamp(currentHP, 0, clampedMax);
            currentHP = Math.Min(clampedMax, before + PlannedHealAmount);
            actualHealed = currentHP - before;
            gold += PlannedBonusGold;

            IsActionCompleted = true;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 현재 영구 덱(MasterDeck)에서 아직 강화되지 않은(!IsUpgraded) 카드 목록을 반환합니다.
        /// distinctByCardId가 true면 서로 다른 종류(CardID)의 카드를 우선 추출합니다.
        /// </summary>
        public List<CardInstance> GetUpgradeableCards(MasterDeck deck, bool distinctByCardId = false)
        {
            var list = new List<CardInstance>();
            if (deck == null) return list;

            HashSet<string> seenIds = distinctByCardId ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
            List<CardInstance> duplicates = distinctByCardId ? new List<CardInstance>() : null;

            foreach (CardInstance card in deck.Cards)
            {
                if (card == null || card.IsUpgraded) continue;

                if (distinctByCardId && !string.IsNullOrEmpty(card.CardID) && !seenIds.Add(card.CardID))
                {
                    duplicates.Add(card);
                    continue;
                }
                list.Add(card);
            }

            if (distinctByCardId && duplicates != null && duplicates.Count > 0)
                list.AddRange(duplicates);

            return list;
        }

        public bool CanSmith(MasterDeck deck)
        {
            if (IsActionCompleted || deck == null) return false;
            foreach (CardInstance card in deck.Cards)
            {
                if (card != null && !card.IsUpgraded)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// [단련 선택지]: 지정한 GUID의 카드를 영구 강화(Upgrade)합니다.
        /// </summary>
        public bool TrySmithCard(MasterDeck deck, string cardInstanceGuid, out CardInstance upgradedCard)
        {
            upgradedCard = null;
            if (IsActionCompleted || deck == null || string.IsNullOrEmpty(cardInstanceGuid))
                return false;

            CardInstance target = deck.FindCardOrNull(cardInstanceGuid);
            if (target == null || !deck.UpgradeCard(cardInstanceGuid))
                return false;

            upgradedCard = target;
            IsActionCompleted = true;
            Changed?.Invoke();
            return true;
        }
    }
}
