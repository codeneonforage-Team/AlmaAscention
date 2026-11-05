using System;
using System.Collections;
using System.Collections.Generic;
using CNF.Data;

namespace CNF.AbilitySystem
{
    /// <summary>
    /// 덱 담당 팀원의 DeckManager와 느슨하게 연결하기 위한 인터페이스.
    /// </summary>
    public interface IDeckOperations
    {
        void DrawCards(int count);
        int ExhaustEntireHand();
        void UpgradeCardsInHand(bool upgradeAll);
        void AddCardToHand(string cardId, bool isUpgraded = false);
    }

    /// <summary>
    /// 카드 시전이나 프리뷰 계산 시 전달되는 전투 문맥 정보.
    /// </summary>
    public struct CombatContext
    {
        public StatusController Caster;
        public StatusController PrimaryTarget;
        public IReadOnlyList<StatusController> AllEnemies;
        public IDeckOperations DeckOps;
        public Func<StatusController, bool> IsTargetIntendingToAttack;
        public int PaidManaCost; // 이번 카드 시전에 실제 소모한 마나량 (X-코스트 카드 배율 계산용)

        public StatusController GetFirstAliveEnemy()
        {
            if (AllEnemies == null) return null;
            for (int i = 0; i < AllEnemies.Count; i++)
            {
                if (AllEnemies[i] != null && AllEnemies[i].IsAlive)
                    return AllEnemies[i];
            }
            return null;
        }

        public List<StatusController> GetValidTargets(ECardTarget targetType, List<StatusController> buffer = null)
        {
            var targets = buffer ?? new List<StatusController>(4);
            targets.Clear();

            switch (targetType)
            {
                case ECardTarget.Self:
                    if (Caster != null && Caster.IsAlive)
                        targets.Add(Caster);
                    break;

                case ECardTarget.SingleEnemy:
                    if (PrimaryTarget != null && PrimaryTarget.IsAlive)
                        targets.Add(PrimaryTarget);
                    break;

                case ECardTarget.AllEnemies:
                    if (AllEnemies != null)
                    {
                        for (int i = 0; i < AllEnemies.Count; i++)
                        {
                            if (AllEnemies[i] != null && AllEnemies[i].IsAlive)
                                targets.Add(AllEnemies[i]);
                        }
                    }
                    break;

                case ECardTarget.RandomEnemy:
                    if (AllEnemies != null)
                    {
                        int aliveCount = 0;
                        for (int i = 0; i < AllEnemies.Count; i++)
                        {
                            if (AllEnemies[i] != null && AllEnemies[i].IsAlive)
                                aliveCount++;
                        }
                        if (aliveCount > 0)
                        {
                            int pick = UnityEngine.Random.Range(0, aliveCount);
                            for (int i = 0; i < AllEnemies.Count; i++)
                            {
                                if (AllEnemies[i] != null && AllEnemies[i].IsAlive)
                                {
                                    if (pick == 0)
                                    {
                                        targets.Add(AllEnemies[i]);
                                        break;
                                    }
                                    pick--;
                                }
                            }
                        }
                    }
                    break;
            }
            return targets;
        }
    }

    /// <summary>
    /// 모든 조립형 카드 효과 블록이 구현하는 공통 인터페이스.
    /// </summary>
    public interface ICardEffect
    {
        void Execute(CombatContext context, CardInstance card);
        IEnumerator ExecuteRoutine(CombatContext context, CardInstance card);
        string FormatDescription(string currentText, CombatContext context, CardInstance card);
    }

    /// <summary>
    /// 신규 카드 효과 블록을 만들 때 상속받기 편하도록 기본 구현과 공통 텍스트 치환 헬퍼를 제공하는 추상 베이스 클래스.
    /// • 인스턴스별 전용 TargetBuffer를 보유하여 GC 할당 0 Byte 및 연쇄 이벤트 재진입 안전성을 보장합니다.
    /// </summary>
    [Serializable]
    public abstract class CardEffectBase : ICardEffect
    {
        [NonSerialized] private List<StatusController> _targetBuffer;
        protected List<StatusController> TargetBuffer => _targetBuffer ??= new List<StatusController>(8);

        public abstract void Execute(CombatContext context, CardInstance card);

        public virtual IEnumerator ExecuteRoutine(CombatContext context, CardInstance card)
        {
            Execute(context, card);
            yield break;
        }

        public virtual string FormatDescription(string currentText, CombatContext context, CardInstance card)
            => currentText;

        public virtual string GetAssignedPlaceholderTag() => null;

        protected static int SelectValue(CardInstance card, int baseVal, int upgradedVal, int bonus = 0)
            => ((card != null && card.IsUpgraded) ? upgradedVal : baseVal) + bonus;

        protected static string ReplaceDamagePlaceholder(string currentText, string customTag, int rawDmg, CombatContext context)
        {
            string tag = string.IsNullOrEmpty(customTag) ? "{D}" : customTag;
            if (string.IsNullOrEmpty(currentText) || !currentText.Contains(tag)) return currentText;

            StatusController previewTarget = context.PrimaryTarget;
            if (previewTarget == null && context.AllEnemies != null && context.AllEnemies.Count == 1)
                previewTarget = context.AllEnemies[0];

            int previewDmg = DamageCalculator.CalculateDamage(rawDmg, context.Caster, previewTarget);
            return currentText.Replace(tag, DamageCalculator.FormatColoredValue(previewDmg, rawDmg));
        }

        protected static string ReplaceBlockPlaceholder(string currentText, string customTag, int rawBlock, CombatContext context)
        {
            string tag = string.IsNullOrEmpty(customTag) ? "{B}" : customTag;
            if (string.IsNullOrEmpty(currentText) || !currentText.Contains(tag)) return currentText;

            int previewBlock = DamageCalculator.CalculatePreviewBlock(rawBlock, context.Caster);
            return currentText.Replace(tag, DamageCalculator.FormatColoredValue(previewBlock, rawBlock));
        }

        protected static string ReplaceValuePlaceholder(string currentText, string customTag, string defaultTag, int value)
        {
            string tag = string.IsNullOrEmpty(customTag) ? defaultTag : customTag;
            if (string.IsNullOrEmpty(currentText) || !currentText.Contains(tag)) return currentText;
            return currentText.Replace(tag, value.ToString());
        }
    }
}
