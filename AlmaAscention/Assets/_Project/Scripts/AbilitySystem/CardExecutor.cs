using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CNF.Data;

namespace CNF.AbilitySystem
{
    /// <summary>
    /// 언리얼의 UCNF_Ability_CardBase를 대체하는 카드 시전 실행기.
    /// • 연속 카드 시전 도중 원래 타겟이 먼저 사망했을 때 자동 리타겟팅(AutoRetarget) 및 전투 종료 시 마나 환불 보호를 지원합니다.
    /// • 카드 소멸(Exhaust) 시 '무감각(FeelNoPain)' 방어도 획득과 '어둠의 포옹(DarkEmbrace)' 드로우를 자동 격발합니다.
    /// </summary>
    public class CardExecutor : MonoBehaviour
    {
        [Header("카드 시전 연출 & 안전 옵션")]
        [Tooltip("카드를 낸 후 실제 타격/효과가 터지기까지의 모션 선딜레이(초)")]
        public float DefaultCastHitDelay = 0.12f;

        [Tooltip("연속 시전 큐 대기 중 원래 지정한 적이 먼저 죽었을 때 살아있는 다른 적으로 자동 타겟 변경 여부")]
        public bool AutoRetargetIfPrimaryDead = true;

        private struct QueuedCardPlay
        {
            public CardInstance Card;
            public CombatContext Context;
            public Action<bool> OnCompleted;
        }

        private readonly Queue<QueuedCardPlay> _playQueue = new();
        private bool _isProcessingQueue;

        // CardExecutor가 직접 소멸 패시브를 격발한 직후, BattleDeckManager.CardExhausted 이벤트에서
        // 동일 카드에 대해 TriggerCardExhaustPassives를 중복 호출하는 것을 방지하는 일회성 가드 GUID
        private static string _pendingDeckExhaustIgnoreGuid;

        // 악마의 불꽃(ExhaustHandDamageEffect) 시전 시 BattleFlow처럼 시전 카드가 아직 손패에 남아있는 상태에서
        // ExhaustEntireHand()가 호출되더라도 자기 자신이 타격 횟수에 포함되지 않도록 방어하는 가드
        private static string _selfExhaustHandCardGuid;
        private static bool _wasSelfExhaustedDuringHandExhaust;

        public bool IsBusy => _isProcessingQueue || _playQueue.Count > 0;

        public event Action<CardInstance, CombatContext> OnBeforeCardPlayed;
        public event Action<CardInstance, CombatContext, bool> OnAfterCardPlayed; // bool = willExhaust

        /// <summary>
        /// 전투 종료 시점이나 강제 턴 종료 시 대기 중인 카드 시전 큐를 안전하게 초기화합니다.
        /// </summary>
        public void ClearQueue()
        {
            while (_playQueue.Count > 0)
            {
                var canceled = _playQueue.Dequeue();
                canceled.OnCompleted?.Invoke(false);
            }
            StopAllCoroutines();
            _isProcessingQueue = false;
            _pendingDeckExhaustIgnoreGuid = null;
            _selfExhaustHandCardGuid = null;
            _wasSelfExhaustedDuringHandExhaust = false;
        }

        public bool CanPlayCard(CardInstance card, CombatContext context, out string failReason)
        {
            failReason = string.Empty;

            if (card == null || card.Data == null)
            {
                failReason = "유효하지 않은 카드입니다.";
                return false;
            }

            if (context.Caster == null || !context.Caster.IsAlive)
            {
                failReason = "시전자가 행동 불능 상태입니다.";
                return false;
            }

            if (card.Data.CardType == ECardType.Status || card.Data.CardType == ECardType.Curse)
            {
                if (card.Data.GetIntParam("Playable", 0) == 0)
                {
                    failReason = "사용할 수 없는 카드입니다.";
                    return false;
                }
            }

            if (!context.Caster.HasEnoughMana(card.CurrentCost))
            {
                failReason = "마나가 부족합니다.";
                return false;
            }

            if (card.Data.TargetType == ECardTarget.SingleEnemy)
            {
                if (context.PrimaryTarget == null || !context.PrimaryTarget.IsAlive)
                {
                    failReason = "유효한 공격 대상이 없습니다.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// [연출 큐 방식] 카드를 낼 때 마나를 즉시 차감하고, 연출 시퀀스 큐에 넣어 순차 실행합니다.
        /// </summary>
        public bool EnqueuePlayCard(CardInstance card, CombatContext context, Action<bool> onCompleted = null)
        {
            if (!CanPlayCard(card, context, out _))
            {
                onCompleted?.Invoke(false);
                return false;
            }

            PayCardMana(card, ref context);

            _playQueue.Enqueue(new QueuedCardPlay
            {
                Card = card,
                Context = context,
                OnCompleted = onCompleted
            });

            if (!_isProcessingQueue)
                StartCoroutine(ProcessCardQueueRoutine());

            return true;
        }

        private IEnumerator ProcessCardQueueRoutine()
        {
            _isProcessingQueue = true;

            while (_playQueue.Count > 0)
            {
                QueuedCardPlay item = _playQueue.Dequeue();
                if (item.Card == null || item.Context.Caster == null || !item.Context.Caster.IsAlive)
                {
                    item.OnCompleted?.Invoke(false);
                    continue;
                }

                CombatContext ctx = item.Context;

                // [UX 방어]: 앞선 카드에 의해 원래 타겟 적이 이미 죽었을 때 처리
                if (item.Card.Data.TargetType == ECardTarget.SingleEnemy &&
                    (ctx.PrimaryTarget == null || !ctx.PrimaryTarget.IsAlive))
                {
                    StatusController newTarget = AutoRetargetIfPrimaryDead ? ctx.GetFirstAliveEnemy() : null;

                    if (newTarget != null)
                    {
                        ctx.PrimaryTarget = newTarget;
                    }
                    else
                    {
                        // 살아있는 적이 전장에 아예 없으면 선차감했던 마나를 환불하고 카드 시전 취소!
                        ctx.Caster.GainMana(ctx.PaidManaCost);
                        item.OnCompleted?.Invoke(false);
                        continue;
                    }
                }

                _pendingDeckExhaustIgnoreGuid = null;
                OnBeforeCardPlayed?.Invoke(item.Card, ctx);

                if (DefaultCastHitDelay > 0f)
                    yield return new WaitForSeconds(DefaultCastHitDelay);

                if (item.Card.Data.Effects != null)
                {
                    for (int i = 0; i < item.Card.Data.Effects.Count; i++)
                    {
                        ICardEffect eff = item.Card.Data.Effects[i];
                        if (eff != null)
                            yield return eff.ExecuteRoutine(ctx, item.Card);
                    }
                }

                FinalizeCardPlay(item.Card, ctx);
                item.OnCompleted?.Invoke(true);
            }

            _isProcessingQueue = false;
        }

        /// <summary>
        /// [즉시 동기 방식] 딜레이 없이 현재 프레임에 즉시 카드를 실행합니다.
        /// </summary>
        public bool TryPlayCard(CardInstance card, CombatContext context)
        {
            if (!CanPlayCard(card, context, out _))
                return false;

            _pendingDeckExhaustIgnoreGuid = null;
            PayCardMana(card, ref context);
            OnBeforeCardPlayed?.Invoke(card, context);

            if (card.Data.Effects != null)
            {
                for (int i = 0; i < card.Data.Effects.Count; i++)
                {
                    card.Data.Effects[i]?.Execute(context, card);
                }
            }

            FinalizeCardPlay(card, context);
            return true;
        }

        private static int PayCardMana(CardInstance card, ref CombatContext context)
        {
            int cost = card.IsXCost ? context.Caster.CurrentMana : card.CurrentCost;
            context.Caster.ConsumeMana(cost);
            context.PaidManaCost = cost;
            return cost;
        }

        private void FinalizeCardPlay(CardInstance card, CombatContext context)
        {
            ExecuteEnchantmentEffects(card, context);

            bool shouldExhaust = card.WillExhaust || card.Data.CardType == ECardType.Power;

            if (shouldExhaust && card.Data.CardType != ECardType.Power)
            {
                _pendingDeckExhaustIgnoreGuid = null;
                TriggerCardExhaustPassives(card, context);
                // OnAfterCardPlayed 직후 BattleDeckManager.ExhaustFromHand가 호출되어
                // CardExhausted 이벤트가 다시 발생하더라도 동일 카드가 중복 발동되지 않도록 일회성 가드 설정
                _pendingDeckExhaustIgnoreGuid = card.InstanceGUID;
            }

            OnAfterCardPlayed?.Invoke(card, context, shouldExhaust);
            CombatEvents.RaiseCardPlayed(card, context, shouldExhaust);
        }

        /// <summary>
        /// 카드가 소멸(Exhaust)될 때 '무감각(FeelNoPain)' 및 '어둠의 포옹(DarkEmbrace)' 파워와 전역 소멸 이벤트를 격발합니다.
        /// (덱 매니저의 CardExhausted 이벤트에 그대로 연결해도 실행기에서 이미 처리한 카드는 자동 중복 방지됨)
        /// </summary>
        public static void TriggerCardExhaustPassives(CardInstance exhaustedCard, CombatContext context)
        {
            if (exhaustedCard != null)
            {
                if (!string.IsNullOrEmpty(_selfExhaustHandCardGuid) &&
                    string.Equals(exhaustedCard.InstanceGUID, _selfExhaustHandCardGuid, StringComparison.Ordinal))
                {
                    _wasSelfExhaustedDuringHandExhaust = true;
                    _selfExhaustHandCardGuid = null;
                    return;
                }

                if (!string.IsNullOrEmpty(_pendingDeckExhaustIgnoreGuid) &&
                    string.Equals(exhaustedCard.InstanceGUID, _pendingDeckExhaustIgnoreGuid, StringComparison.Ordinal))
                {
                    _pendingDeckExhaustIgnoreGuid = null;
                    return;
                }
            }

            if (context.Caster != null && context.Caster.IsAlive)
            {
                int feelNoPain = context.Caster.GetStatus(StatusKeys.FeelNoPain);
                if (feelNoPain > 0)
                    context.Caster.AddBlock(feelNoPain, applyDexterity: false);

                int darkEmbrace = context.Caster.GetStatus(StatusKeys.DarkEmbrace);
                if (darkEmbrace > 0 && context.DeckOps != null)
                    context.DeckOps.DrawCards(darkEmbrace);
            }

            CombatEvents.RaiseCardExhausted(exhaustedCard, context.Caster);
        }

        internal static void BeginSelfExhaustHandGuard(CardInstance castingCard)
        {
            _selfExhaustHandCardGuid = castingCard?.InstanceGUID;
            _wasSelfExhaustedDuringHandExhaust = false;
        }

        internal static bool EndSelfExhaustHandGuard()
        {
            _selfExhaustHandCardGuid = null;
            bool wasSelfExhausted = _wasSelfExhaustedDuringHandExhaust;
            _wasSelfExhaustedDuringHandExhaust = false;
            return wasSelfExhausted;
        }

        // =====================================================================
        // 포션(Potion) & 성유물(Relic) 실행 연동 브릿지 (PotionManager / RelicManager 연동용)
        // =====================================================================

        /// <summary>
        /// 포션을 현재 전투 컨텍스트에서 수동으로 사용할 수 있는지 검사합니다.
        /// </summary>
        public static bool CanUsePotion(PotionDataSO potion, CombatContext context, out string failReason)
        {
            failReason = string.Empty;

            if (potion == null)
            {
                failReason = "유효하지 않은 포션입니다.";
                return false;
            }

            if (context.Caster == null || !context.Caster.IsAlive)
            {
                failReason = "사용자가 행동 불능 상태입니다.";
                return false;
            }

            if (potion.EffectType == EPotionEffectType.Resurrect)
            {
                failReason = "치명상 시 자동 발동하는 부활 포션입니다.";
                return false;
            }

            bool needsAliveEnemy = potion.TargetType == ECardTarget.SingleEnemy
                                || potion.TargetType == ECardTarget.AllEnemies
                                || potion.TargetType == ECardTarget.RandomEnemy
                                || potion.EffectType == EPotionEffectType.DamageSingle
                                || potion.EffectType == EPotionEffectType.DamageAll;

            if (needsAliveEnemy && ResolveSingleEnemy(context) == null)
            {
                failReason = "유효한 대상 적이 없습니다.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// PotionDataSO의 설정값에 따라 즉시 포션 효과를 실행합니다. (PotionManager.UsePotion 연동용)
        /// </summary>
        public static bool TryExecutePotion(PotionDataSO potion, CombatContext context)
        {
            if (!CanUsePotion(potion, context, out _))
                return false;

            StatusController caster = context.Caster;
            StatusController singleTarget = ResolveSingleEnemy(context);

            int stacks = potion.StatusStacks != 0 ? potion.StatusStacks : potion.EffectValue;

            switch (potion.EffectType)
            {
                case EPotionEffectType.Heal:
                    caster.Heal(potion.EffectValue);
                    return true;

                case EPotionEffectType.Block:
                    caster.AddBlock(potion.EffectValue, applyDexterity: false);
                    return true;

                case EPotionEffectType.BuffStrength:
                    caster.AddStatus(StatusKeys.Strength, stacks);
                    return true;

                case EPotionEffectType.BuffDexterity:
                    caster.AddStatus(StatusKeys.Dexterity, stacks);
                    return true;

                case EPotionEffectType.DamageSingle:
                    if (singleTarget == null) return false;
                    singleTarget.TakeDamage(potion.EffectValue, caster);
                    return true;

                case EPotionEffectType.DamageAll:
                    DealFixedDamageToAllEnemies(context, potion.EffectValue);
                    return true;

                case EPotionEffectType.DebuffWeak:
                    ApplyStatusByTargetType(StatusKeys.Weak, stacks, potion.TargetType, context);
                    return true;

                case EPotionEffectType.DebuffVulnerable:
                    ApplyStatusByTargetType(StatusKeys.Vulnerable, stacks, potion.TargetType, context);
                    return true;

                case EPotionEffectType.DebuffPoison:
                    ApplyStatusByTargetType(StatusKeys.Poison, stacks, potion.TargetType, context);
                    return true;

                case EPotionEffectType.DrawCards:
                    if (context.DeckOps == null) return false;
                    context.DeckOps.DrawCards(potion.EffectValue);
                    return true;

                case EPotionEffectType.GainMana:
                    caster.GainMana(potion.EffectValue);
                    return true;

                case EPotionEffectType.Custom:
                    if (!string.IsNullOrEmpty(potion.StatusID) && stacks != 0)
                    {
                        ApplyStatusByTargetType(potion.StatusID, stacks, potion.TargetType, context);
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 치명상(HP == 0) 발생 시 요정 포션(EPotionEffectType.Resurrect)으로 부활을 처리합니다.
        /// (StatusController.OnTryInterceptFatalDamage 콜백 내부에서 호출)
        /// </summary>
        public static bool TryExecuteResurrectPotion(PotionDataSO potion, StatusController dyingTarget)
        {
            if (potion == null || potion.EffectType != EPotionEffectType.Resurrect || dyingTarget == null)
                return false;

            int percent = potion.EffectValue > 0 ? potion.EffectValue : 30;
            int healAmount = Mathf.Max(1, Mathf.RoundToInt(dyingTarget.MaxHP * (percent / 100f)));
            dyingTarget.Heal(healAmount);
            return dyingTarget.CurrentHP > 0;
        }

        /// <summary>
        /// 성유물 획득 즉시 적용되는 영구 스탯 보너스(최대 체력, 즉시 회복, 골드)를 실행합니다. (RelicManager.OnRelicAdded 연동용)
        /// </summary>
        public static void ApplyRelicAcquireBonus(RelicDataSO relicData, StatusController player, Action<int> onGainGold = null)
        {
            if (relicData == null || player == null) return;

            if (relicData.AcquireMaxHealthBonus != 0)
                player.AddMaxHP(relicData.AcquireMaxHealthBonus, healAddedAmount: true);

            if (relicData.AcquireHealBonus > 0)
                player.Heal(relicData.AcquireHealBonus);

            if (relicData.AcquireGoldBonus != 0)
                onGainGold?.Invoke(relicData.AcquireGoldBonus);
        }

        /// <summary>
        /// 성유물의 발동 시점(TriggerType)이 일치할 때 RelicDataSO에 정의된 전투 효과를 실행합니다. (RelicManager.TriggerRelics 연동용)
        /// </summary>
        public static bool TryTriggerRelicEffect(RelicDataSO relicData, ERelicTriggerType triggerTiming, CombatContext context)
        {
            if (relicData == null || relicData.TriggerType != triggerTiming)
                return false;

            StatusController caster = context.Caster;
            if (caster == null || !caster.IsAlive)
                return false;

            bool executed = false;
            string effectKey = relicData.EffectKey ?? string.Empty;
            int stacks = relicData.StatusStacks != 0 ? relicData.StatusStacks : relicData.EffectValue;

            if (!string.IsNullOrEmpty(relicData.StatusID) && stacks != 0)
            {
                bool targetAllEnemies = effectKey.IndexOf("All", StringComparison.OrdinalIgnoreCase) >= 0;
                bool targetRandomEnemy = !targetAllEnemies &&
                    effectKey.IndexOf("Random", StringComparison.OrdinalIgnoreCase) >= 0;
                bool targetSingleEnemy = !targetAllEnemies && !targetRandomEnemy &&
                    (effectKey.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     effectKey.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0);

                ECardTarget targetMode = targetAllEnemies
                    ? ECardTarget.AllEnemies
                    : (targetRandomEnemy
                        ? ECardTarget.RandomEnemy
                        : (targetSingleEnemy ? ECardTarget.SingleEnemy : ECardTarget.Self));

                ApplyStatusByTargetType(relicData.StatusID, stacks, targetMode, context);
                executed = true;
            }

            if (!string.IsNullOrEmpty(effectKey) && relicData.EffectValue != 0)
            {
                if (string.Equals(effectKey, "GainBlock", StringComparison.OrdinalIgnoreCase))
                {
                    caster.AddBlock(relicData.EffectValue, applyDexterity: false);
                    executed = true;
                }
                else if (string.Equals(effectKey, "Heal", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(effectKey, "HealOnWin", StringComparison.OrdinalIgnoreCase))
                {
                    caster.Heal(relicData.EffectValue);
                    executed = true;
                }
                else if (string.Equals(effectKey, "GainMana", StringComparison.OrdinalIgnoreCase))
                {
                    caster.GainMana(relicData.EffectValue);
                    executed = true;
                }
                else if (string.Equals(effectKey, "DrawCards", StringComparison.OrdinalIgnoreCase) && context.DeckOps != null)
                {
                    context.DeckOps.DrawCards(relicData.EffectValue);
                    executed = true;
                }
                else if (string.Equals(effectKey, "DamageAll", StringComparison.OrdinalIgnoreCase))
                {
                    DealFixedDamageToAllEnemies(context, relicData.EffectValue);
                    executed = true;
                }
                else if (string.Equals(effectKey, "ApplyWeakAll", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(relicData.StatusID))
                {
                    ApplyStatusByTargetType(StatusKeys.Weak, stacks, ECardTarget.AllEnemies, context);
                    executed = true;
                }
                else if (string.Equals(effectKey, "ApplyVulnerableAll", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(relicData.StatusID))
                {
                    ApplyStatusByTargetType(StatusKeys.Vulnerable, stacks, ECardTarget.AllEnemies, context);
                    executed = true;
                }
            }

            return executed;
        }

        private static StatusController ResolveSingleEnemy(CombatContext context)
        {
            return (context.PrimaryTarget != null && context.PrimaryTarget.IsAlive)
                ? context.PrimaryTarget
                : context.GetFirstAliveEnemy();
        }

        private static void DealFixedDamageToAllEnemies(CombatContext context, int damage)
        {
            if (damage <= 0 || context.AllEnemies == null) return;
            for (int i = 0; i < context.AllEnemies.Count; i++)
            {
                var enemy = context.AllEnemies[i];
                if (enemy != null && enemy.IsAlive)
                    enemy.TakeDamage(damage, context.Caster);
            }
        }

        private static readonly List<StatusController> _randomTargetPickBuffer = new(1);

        private static void ApplyStatusByTargetType(
            string statusId,
            int stacks,
            ECardTarget targetType,
            CombatContext context)
        {
            if (string.IsNullOrEmpty(statusId) || stacks == 0) return;

            switch (targetType)
            {
                case ECardTarget.None:
                case ECardTarget.Self:
                    context.Caster?.AddStatus(statusId, stacks);
                    break;

                case ECardTarget.SingleEnemy:
                    ResolveSingleEnemy(context)?.AddStatus(statusId, stacks);
                    break;

                case ECardTarget.AllEnemies:
                    if (context.AllEnemies != null)
                    {
                        for (int i = 0; i < context.AllEnemies.Count; i++)
                        {
                            var enemy = context.AllEnemies[i];
                            if (enemy != null && enemy.IsAlive)
                                enemy.AddStatus(statusId, stacks);
                        }
                    }
                    break;

                case ECardTarget.RandomEnemy:
                    var picked = context.GetValidTargets(ECardTarget.RandomEnemy, _randomTargetPickBuffer);
                    StatusController randomEnemy = picked.Count > 0 ? picked[0] : null;
                    randomEnemy?.AddStatus(statusId, stacks);
                    break;
            }
        }

        private void ExecuteEnchantmentEffects(CardInstance card, CombatContext context)
        {
            if (card == null || string.IsNullOrEmpty(card.EnchantmentID)) return;

            if (DataManager.Instance != null)
            {
                var customEnchant = DataManager.Instance.GetEnchantmentBehavior(card.EnchantmentID);
                if (customEnchant != null)
                {
                    customEnchant.OnCardPlayed(context, card);
                    return;
                }
            }

            if (string.Equals(card.EnchantmentID, EnchantmentKeys.Vitality, StringComparison.OrdinalIgnoreCase))
            {
                context.Caster?.AddStatus(StatusKeys.Strength, 1);
            }
            else if (string.Equals(card.EnchantmentID, EnchantmentKeys.SoulAwaken, StringComparison.OrdinalIgnoreCase))
            {
                context.Caster?.AddMaxHP(2, healAddedAmount: true);
            }
            else if (string.Equals(card.EnchantmentID, EnchantmentKeys.DeadlyPoison, StringComparison.OrdinalIgnoreCase))
            {
                ResolveSingleEnemy(context)?.AddStatus(StatusKeys.Poison, 2);
            }
        }
    }
}
