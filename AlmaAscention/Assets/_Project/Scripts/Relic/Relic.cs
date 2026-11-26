using System;
using System.Collections.Generic;
using CNF.Data;
using CNF.AbilitySystem;
using AlmaAscention.Deck;

namespace AlmaAscention.Relic
{
    // Run에 필요한 부분만 담당하는 Relic
    // ex) 전투 N회 후 사용을 멈추는 Relic 혹은 1회성 발동 Relic 등
    public sealed class Relic
    {
        // CardInstance.CombatState와 같은 방식으로 원래 객체와 그 시점의 런타임 값을 보존한다.
        public readonly struct CombatState
        {
            public readonly Relic Relic;
            public readonly bool Expired;
            public readonly long ElapsedPlayerTurns;
            public readonly int CooldownTurns, CooldownBattles, CardsPlayedThisTurn;
            internal readonly (StatusController Owner, string StatusId, int Amount)[] TurnBonuses;
            internal readonly (CardInstance Card, int PreviousCost, int AppliedCost)[] TurnCosts;
            internal readonly bool ActivatedThisTurn;

            public CombatState(Relic relic)
            {
                Relic = relic;
                Expired = relic.IsExpired;
                ElapsedPlayerTurns = relic.ElapsedPlayerTurns;
                CooldownTurns = relic.RemainingCooldownTurns;
                CooldownBattles = relic.RemainingCooldownBattles;
                CardsPlayedThisTurn = relic._cardsPlayedThisTurn;
                TurnBonuses = relic._turnBonuses.ToArray();
                TurnCosts = relic._turnCosts.ToArray();
                ActivatedThisTurn = relic._activatedThisTurn;
            }
        }

        public CombatState CaptureCombatState()
        {
            return new CombatState(this);
        }

        // 실제 캐릭터 능력치는 호출자가 StatusSnapshot으로 함께 복원한다.
        // 여기서는 효과 재적용·해제 없이 회수할 보너스 기록 등 내부 상태만 복원한다.
        public void RestoreCombatState(CombatState state)
        {
            if (state.Relic != this)
            {
                throw new InvalidOperationException("다른 유물의 상태를 복원할 수 없습니다.");
            }

            IsExpired = state.Expired;
            ElapsedPlayerTurns = state.ElapsedPlayerTurns;
            RemainingCooldownTurns = state.CooldownTurns;
            RemainingCooldownBattles = state.CooldownBattles;
            _cardsPlayedThisTurn = state.CardsPlayedThisTurn;
            _turnBonuses.Clear();
            _turnBonuses.AddRange(state.TurnBonuses);
            _turnCosts.Clear();
            _turnCosts.AddRange(state.TurnCosts);
            _activatedThisTurn = state.ActivatedThisTurn;
        }

        public readonly RelicDataSO OriginalRelicData;

        public bool IsExpired { get; private set; }
        // 획득 이후 누적 플레이어 턴 수. 전투 종료 시 초기화하지 않는다.
        public long ElapsedPlayerTurns { get; private set; }
        public int RemainingCooldownTurns { get; private set; }
        private int _cardsPlayedThisTurn;
        private bool _activatedThisTurn;
        private readonly List<(StatusController Owner, string StatusId, int Amount)> _turnBonuses = new List<(StatusController, string, int)>();
        private readonly List<(CardInstance Card, int PreviousCost, int AppliedCost)> _turnCosts = new List<(CardInstance, int, int)>();

        internal void AdvancePlayerTurn()
        {
            ElapsedPlayerTurns++;
            _cardsPlayedThisTurn = 0;
            _activatedThisTurn = false;
            if (RemainingCooldownTurns > 0)
            {
                RemainingCooldownTurns--;
            }
        }

        internal void RecordCardPlayed()
        {
            _cardsPlayedThisTurn++;
        }
        public int RemainingCooldownBattles { get; private set; }
        public bool IsOnCooldown => RemainingCooldownBattles > 0;

        // 선조건: relicData는 유효하다.
        public Relic(RelicDataSO relicData)
        {
            OriginalRelicData = relicData;
        }

        // 유물 ID·이름 대신 데이터의 조건과 효과 규격을 읽는다.
        // 기존 AbilitySystem에 없는 집계 피해·복합 상태·턴 한정 상태만 여기서 조합한다.
        // CustomParams: TurnInterval=누적 턴 주기, FirstTurnOnly=true/false, CooldownTurns=발동 후 대기 턴.
        // DamageRandom: ValueSource=CardsPlayedThisTurn이면 카드 수 × EffectValue 피해.
        // ApplyStatus: Target=ECardTarget 이름, AdditionalStatusIDs=쉼표 구분 상태 ID, Duration=Turn이면 턴 종료 시 회수.
        // Duration을 지정하지 않으면 상태 자체의 기존 지속 규칙을 따른다.
        // SetHandCost: Duration=Turn, EffectValue=적용 비용. 발동 시점 손패만 대상으로 한다.
        internal bool TryTrigger(ERelicTriggerType timing, CombatContext context, int battleTurnNumber)
        {
            if (OriginalRelicData.TriggerType != timing || context.Caster == null || !context.Caster.IsAlive || RemainingCooldownTurns > 0)
            {
                return false;
            }

            if (OriginalRelicData.GetBoolParam("FirstTurnOnly") && battleTurnNumber != 1)
            {
                return false;
            }

            int interval = OriginalRelicData.GetIntParam("TurnInterval", 0);
            if (interval > 0 && (ElapsedPlayerTurns == 0 || ElapsedPlayerTurns % interval != 0))
            {
                return false;
            }

            bool executed;
            string key = OriginalRelicData.EffectKey;
            if (string.Equals(key, "DamageRandom", StringComparison.OrdinalIgnoreCase) && string.Equals(OriginalRelicData.GetParam("ValueSource"), "CardsPlayedThisTurn", StringComparison.OrdinalIgnoreCase))
            {
                int damage = _cardsPlayedThisTurn * OriginalRelicData.EffectValue;
                if (damage <= 0)
                {
                    return false;
                }

                List<StatusController> targets = context.GetValidTargets(ECardTarget.RandomEnemy);
                if (targets.Count == 0)
                {
                    return false;
                }

                _cardsPlayedThisTurn = 0;
                targets[0].TakeDamage(damage, context.Caster);
                executed = true;
            }
            else if (string.Equals(key, "ApplyStatus", StringComparison.OrdinalIgnoreCase))
            {
                executed = ApplyConfiguredStatuses(context);
            }
            else if (string.Equals(key, "SetHandCost", StringComparison.OrdinalIgnoreCase))
            {
                executed = ApplyHandCost(context);
            }
            else if (string.Equals(key, "ApplyVulnerableToSelectedTarget", StringComparison.OrdinalIgnoreCase))
            {
                executed = ApplyVulnerableToSelectedTarget(context);
            }
            else
            {
                executed = CardExecutor.TryTriggerRelicEffect(OriginalRelicData, timing, context);
            }

            if (executed)
            {
                RemainingCooldownTurns = Math.Max(0, OriginalRelicData.GetIntParam("CooldownTurns", 0));
            }

            return executed;
        }

        private bool ApplyVulnerableToSelectedTarget(CombatContext context)
        {
            if (_activatedThisTurn || context.PrimaryTarget == null || !context.PrimaryTarget.IsAlive || OriginalRelicData.StatusStacks <= 0 || string.IsNullOrEmpty(OriginalRelicData.StatusID))
            {
                return false;
            }

            context.PrimaryTarget.AddStatus(OriginalRelicData.StatusID, OriginalRelicData.StatusStacks);
            context.Caster.GainMana(OriginalRelicData.GetIntParam("ManaGain", 0));
            _activatedThisTurn = true;
            return true;
        }

        private bool ApplyHandCost(CombatContext context)
        {
            // IDeckOperations에는 손패 조회가 없어 기존 전투 덱 매니저의 읽기 전용 목록을 사용한다.
            if (!(context.DeckOps is BattleDeckManager deck) || _turnCosts.Count > 0 || OriginalRelicData.EffectValue < 0 || !string.Equals(OriginalRelicData.GetParam("Duration"), "Turn", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int cost = OriginalRelicData.EffectValue;
            foreach (CardInstance card in deck.HandCards)
            {
                // X 코스트는 기존 실행기가 남은 행동치 전부를 소비하므로 임시 비용만으로 변경할 수 없다.
                if (card.IsXCost || card.TempCostOverride == cost)
                {
                    continue;
                }

                _turnCosts.Add((card, card.TempCostOverride, cost));
                card.TempCostOverride = cost;
            }

            return _turnCosts.Count > 0;
        }

        private bool ApplyConfiguredStatuses(CombatContext context)
        {
            bool temporary = string.Equals(OriginalRelicData.GetParam("Duration"), "Turn", StringComparison.OrdinalIgnoreCase);
            if (temporary && _turnBonuses.Count > 0)
            {
                return false;
            }

            if (!Enum.TryParse(OriginalRelicData.GetParam("Target", "Self"), true, out ECardTarget targetType))
            {
                return false;
            }

            int stacks = OriginalRelicData.StatusStacks != 0 ? OriginalRelicData.StatusStacks : OriginalRelicData.EffectValue;
            if (stacks == 0 || string.IsNullOrWhiteSpace(OriginalRelicData.StatusID))
            {
                return false;
            }

            List<string> statusIds = new List<string> { OriginalRelicData.StatusID };
            foreach (string statusId in OriginalRelicData.GetParam("AdditionalStatusIDs").Split(','))
            {
                string trimmed = statusId.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !statusIds.Exists(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    statusIds.Add(trimmed);
                }
            }

            bool executed = false;
            foreach (StatusController target in context.GetValidTargets(targetType))
            {
                foreach (string statusId in statusIds)
                {
                    int before = target.GetStatus(statusId);
                    target.AddStatus(statusId, stacks);
                    int applied = target.GetStatus(statusId) - before;
                    if (temporary && applied != 0)
                    {
                        // 적용 당시 대상·상태·실제 변화량을 저장하므로 데이터 수정 후에도 정확히 회수한다.
                        _turnBonuses.Add((target, statusId, applied));
                    }

                    executed = true;
                }
            }

            return executed;
        }

        // 인공물이 보너스 회수를 막지 않도록 상태 부여 대신 현재 값에서 차감한다.
        internal void ClearTurnBonus()
        {
            var costs = _turnCosts.ToArray();
            _turnCosts.Clear();
            foreach (var entry in costs)
            {
                // 이후 다른 효과가 비용을 덮어썼다면 그 변경은 보존한다.
                if (entry.Card.TempCostOverride == entry.AppliedCost)
                {
                    entry.Card.TempCostOverride = entry.PreviousCost;
                }
            }

            var bonuses = _turnBonuses.ToArray();
            _turnBonuses.Clear();
            foreach (var bonus in bonuses)
            {
                if (bonus.Owner != null)
                {
                    bonus.Owner.SetStatus(bonus.StatusId, bonus.Owner.GetStatus(bonus.StatusId) - bonus.Amount);
                }
            }
        }
        internal void EndTurn()
        {
            ClearTurnBonus();
            _cardsPlayedThisTurn = 0;
        }

        // 같은 어셈블리 내부에서만 상태 변경을 허용한다.
        internal void Expire()
        {
            IsExpired = true;
            ClearTurnBonus();
        }

        internal void StartBattleCooldown()
        {
            if (OriginalRelicData != null && OriginalRelicData.CooldownBattles > 0)
            {
                RemainingCooldownBattles = OriginalRelicData.CooldownBattles;
            }
        }

        internal void AdvanceBattleCooldown()
        {
            if (RemainingCooldownBattles > 0)
            {
                RemainingCooldownBattles--;
            }
        }

    }

}
