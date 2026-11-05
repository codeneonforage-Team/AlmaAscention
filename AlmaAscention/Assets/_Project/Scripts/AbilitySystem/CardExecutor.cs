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
                TriggerCardExhaustPassives(card, context);
            }

            OnAfterCardPlayed?.Invoke(card, context, shouldExhaust);
            CombatEvents.RaiseCardPlayed(card, context, shouldExhaust);
        }

        /// <summary>
        /// 카드가 소멸(Exhaust)될 때 '무감각(FeelNoPain)' 및 '어둠의 포옹(DarkEmbrace)' 파워와 전역 소멸 이벤트를 격발합니다.
        /// (덱 매니저에서 휘발성이나 악마의 불꽃으로 카드를 소멸시킬 때도 호출 가능)
        /// </summary>
        public static void TriggerCardExhaustPassives(CardInstance exhaustedCard, CombatContext context)
        {
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
                context.PrimaryTarget?.AddStatus(StatusKeys.Poison, 2);
            }
        }
    }
}
