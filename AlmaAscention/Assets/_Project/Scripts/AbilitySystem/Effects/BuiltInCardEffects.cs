using System;
using System.Collections;
using UnityEngine;
using CNF.Data;

namespace CNF.AbilitySystem
{
    /// <summary>
    /// [기본 블록 1] 일반/다단/광역 공격 피해 효과 (GenericAttack 기본)
    /// • PlaceholderTag(기본 "{D}")를 통해 한 카드에 공격 블록이 2개 이상일 때 "{D}", "{D2}"로 구분 치환할 수 있습니다.
    /// </summary>
    [Serializable]
    public class DealDamageEffect : CardEffectBase
    {
        public int BaseDamage = 6;
        public int UpgradedDamage = 9;
        public int HitCount = 1;
        [Tooltip("X-코스트 카드일 때 지불한 마나(X)만큼 타격 횟수(HitCount)를 곱할지 여부 (예: 소용돌이 Whirlwind)")]
        public bool MultiplyHitsByXCost = false;
        [Tooltip("다단히트(HitCount > 1) 시 타격 간 시간차(초)")]
        public float HitInterval = 0.1f;
        [Tooltip("단일 타겟 다단히트 도중 대상이 사망하면 남은 타수를 다른 생존 적에게 자동으로 이어서 타격할지 여부")]
        public bool RetargetIfTargetDiesMidCombo = false;
        public ECardTarget OverrideTarget = ECardTarget.SingleEnemy;
        [Tooltip("카드 설명에서 치환할 태그 (기본: {D}, 두 번째 공격은 {D2} 등으로 지정 가능)")]
        public string PlaceholderTag = "{D}";

        public override void Execute(CombatContext context, CardInstance card)
        {
            int rawDmg = GetRawDamage(card);
            ECardTarget targetMode = card != null ? card.Data.TargetType : OverrideTarget;
            int hits = GetHitCount(context);

            for (int hit = 0; hit < hits; hit++)
            {
                if (!PerformSingleHitStep(ref context, rawDmg, targetMode))
                    break;
            }
        }

        public override IEnumerator ExecuteRoutine(CombatContext context, CardInstance card)
        {
            int rawDmg = GetRawDamage(card);
            ECardTarget targetMode = card != null ? card.Data.TargetType : OverrideTarget;
            int hits = GetHitCount(context);

            for (int hit = 0; hit < hits; hit++)
            {
                if (!PerformSingleHitStep(ref context, rawDmg, targetMode))
                    yield break;

                if (hit < hits - 1 && HitInterval > 0f)
                    yield return new WaitForSeconds(HitInterval);
            }
        }

        private int GetRawDamage(CardInstance card)
            => SelectValue(card, BaseDamage, UpgradedDamage, card != null ? card.BonusDamage : 0);

        private int GetHitCount(CombatContext context)
        {
            return MultiplyHitsByXCost
                ? Mathf.Max(0, HitCount * context.PaidManaCost)
                : Mathf.Max(1, HitCount);
        }

        private bool PerformSingleHitStep(ref CombatContext context, int rawDmg, ECardTarget targetMode)
        {
            if (RetargetIfTargetDiesMidCombo && targetMode == ECardTarget.SingleEnemy &&
                (context.PrimaryTarget == null || !context.PrimaryTarget.IsAlive))
            {
                context.PrimaryTarget = context.GetFirstAliveEnemy();
            }

            var targets = context.GetValidTargets(targetMode, TargetBuffer);
            if (targets.Count == 0) return false;

            for (int i = 0; i < targets.Count; i++)
            {
                StatusController target = targets[i];
                if (target == null || !target.IsAlive) continue;

                int finalDmg = DamageCalculator.CalculateDamage(rawDmg, context.Caster, target, ignoreStunCheck: true);
                target.TakeDamage(finalDmg, context.Caster);
            }
            return true;
        }

        public override string FormatDescription(string currentText, CombatContext context, CardInstance card)
            => ReplaceDamagePlaceholder(currentText, PlaceholderTag, GetRawDamage(card), context);

        public override string GetAssignedPlaceholderTag() => string.IsNullOrEmpty(PlaceholderTag) ? "{D}" : PlaceholderTag;
    }

    /// <summary>
    /// [기본 블록 2] 방어도(Block) 획득 효과 (GenericSkill 기본)
    /// </summary>
    [Serializable]
    public class GainBlockEffect : CardEffectBase
    {
        public int BaseBlock = 5;
        public int UpgradedBlock = 8;
        [Tooltip("X-코스트 카드일 때 지불한 마나(X)회만큼 방어도를 반복 획득할지 여부 (예: 강화된 육체 Reinforced Body)")]
        public bool MultiplyByXCost = false;
        [Tooltip("카드 설명에서 치환할 태그 (기본: {B})")]
        public string PlaceholderTag = "{B}";

        public override void Execute(CombatContext context, CardInstance card)
        {
            if (context.Caster == null) return;
            int rawBlock = GetRawBlock(card);
            int times = MultiplyByXCost ? Mathf.Max(0, context.PaidManaCost) : 1;

            for (int i = 0; i < times; i++)
            {
                context.Caster.AddBlock(rawBlock, applyDexterity: true);
            }
        }

        private int GetRawBlock(CardInstance card)
            => SelectValue(card, BaseBlock, UpgradedBlock, card != null ? card.BonusBlock : 0);

        public override string FormatDescription(string currentText, CombatContext context, CardInstance card)
            => ReplaceBlockPlaceholder(currentText, PlaceholderTag, GetRawBlock(card), context);

        public override string GetAssignedPlaceholderTag() => string.IsNullOrEmpty(PlaceholderTag) ? "{B}" : PlaceholderTag;
    }

    /// <summary>
    /// [기본 블록 3] 상태이상(버프/디버프/파워) 부여 효과
    /// • 한 카드에 상태이상 블록이 2개 이상일 때 첫 번째는 "{M}", 두 번째는 "{M2}"로 지정하면 각각 정확히 치환됩니다.
    /// </summary>
    [Serializable]
    public class ApplyStatusEffect : CardEffectBase
    {
        [StatusIdSelector]
        public string StatusID = StatusKeys.Vulnerable;
        public int BaseStacks = 2;
        public int UpgradedStacks = 3;
        [Tooltip("반복 부여 횟수 (예: 무작위 적에게 중독 2를 3번 튕기며 부여할 때 3으로 설정)")]
        public int RepeatCount = 1;
        [Tooltip("RepeatCount > 1일 때 반복 부여 간격(초)")]
        public float HitInterval = 0.08f;
        public ECardTarget ApplyTo = ECardTarget.SingleEnemy;
        [Tooltip("카드 설명에서 치환할 태그 (기본: {M}, 두 번째 상태이상은 {M2} 등으로 지정 가능)")]
        public string PlaceholderTag = "{M}";

        public override void Execute(CombatContext context, CardInstance card)
        {
            int stacks = GetStacks(card);
            if (stacks == 0) return;

            int repeats = Mathf.Max(1, RepeatCount);
            for (int r = 0; r < repeats; r++)
            {
                if (!PerformApplyStep(context, stacks)) break;
            }
        }

        public override IEnumerator ExecuteRoutine(CombatContext context, CardInstance card)
        {
            int stacks = GetStacks(card);
            if (stacks == 0) yield break;

            int repeats = Mathf.Max(1, RepeatCount);
            for (int r = 0; r < repeats; r++)
            {
                if (!PerformApplyStep(context, stacks)) yield break;

                if (r < repeats - 1 && HitInterval > 0f)
                    yield return new WaitForSeconds(HitInterval);
            }
        }

        private int GetStacks(CardInstance card)
            => SelectValue(card, BaseStacks, UpgradedStacks);

        private bool PerformApplyStep(CombatContext context, int stacks)
        {
            var targets = context.GetValidTargets(ApplyTo, TargetBuffer);
            if (targets.Count == 0) return false;

            for (int i = 0; i < targets.Count; i++)
            {
                targets[i]?.AddStatus(StatusID, stacks);
            }
            return true;
        }

        public override string FormatDescription(string currentText, CombatContext context, CardInstance card)
            => ReplaceValuePlaceholder(currentText, PlaceholderTag, "{M}", Mathf.Abs(GetStacks(card)));

        public override string GetAssignedPlaceholderTag() => string.IsNullOrEmpty(PlaceholderTag) ? "{M}" : PlaceholderTag;
    }

    /// <summary>
    /// [기본 블록 4] 카드 드로우 및 마나 회복 효과
    /// </summary>
    [Serializable]
    public class DrawAndManaEffect : CardEffectBase
    {
        public int BaseDraw = 1;
        public int UpgradedDraw = 2;
        public int BaseManaGain = 0;
        public int UpgradedManaGain = 0;
        [Tooltip("카드 설명에서 드로우/마나 수치를 치환할 태그 (기본: {M})")]
        public string PlaceholderTag = "{M}";

        public override void Execute(CombatContext context, CardInstance card)
        {
            int draw = SelectValue(card, BaseDraw, UpgradedDraw);
            int mana = SelectValue(card, BaseManaGain, UpgradedManaGain);

            if (mana > 0 && context.Caster != null)
                context.Caster.GainMana(mana);

            if (draw > 0 && context.DeckOps != null)
                context.DeckOps.DrawCards(draw);
        }

        public override string FormatDescription(string currentText, CombatContext context, CardInstance card)
        {
            int draw = SelectValue(card, BaseDraw, UpgradedDraw);
            int mana = SelectValue(card, BaseManaGain, UpgradedManaGain);
            int displayVal = draw > 0 ? draw : mana;
            return ReplaceValuePlaceholder(currentText, PlaceholderTag, "{M}", displayVal);
        }

        public override string GetAssignedPlaceholderTag() => string.IsNullOrEmpty(PlaceholderTag) ? "{M}" : PlaceholderTag;
    }

    /// <summary>
    /// [특수 기믹 블록 1] 보디슬램(BodySlam): 현재 방어도만큼 적에게 피해를 줍니다.
    /// </summary>
    [Serializable]
    public class BodySlamDamageEffect : CardEffectBase
    {
        public string PlaceholderTag = "{D}";

        public override void Execute(CombatContext context, CardInstance card)
        {
            if (context.Caster == null || context.PrimaryTarget == null) return;
            int rawDmg = context.Caster.Block;
            int finalDmg = DamageCalculator.CalculateDamage(rawDmg, context.Caster, context.PrimaryTarget, ignoreStunCheck: true);
            context.PrimaryTarget.TakeDamage(finalDmg, context.Caster);
        }

        public override string FormatDescription(string currentText, CombatContext context, CardInstance card)
            => ReplaceDamagePlaceholder(currentText, PlaceholderTag, context.Caster != null ? context.Caster.Block : 0, context);

        public override string GetAssignedPlaceholderTag() => string.IsNullOrEmpty(PlaceholderTag) ? "{D}" : PlaceholderTag;
    }

    /// <summary>
    /// [특수 기믹 블록 2] 사신의 낫(Reaper): 모든 적에게 피해를 주고 실제 깎은 HP만큼 시전자를 회복합니다.
    /// </summary>
    [Serializable]
    public class VampireAoEDamageEffect : CardEffectBase
    {
        public int BaseDamage = 4;
        public int UpgradedDamage = 5;
        public string PlaceholderTag = "{D}";

        public override void Execute(CombatContext context, CardInstance card)
        {
            int rawDmg = GetRawDamage(card);
            var enemies = context.GetValidTargets(ECardTarget.AllEnemies, TargetBuffer);
            int totalUnblockedDamageDealt = 0;

            for (int i = 0; i < enemies.Count; i++)
            {
                int finalDmg = DamageCalculator.CalculateDamage(rawDmg, context.Caster, enemies[i], ignoreStunCheck: true);
                totalUnblockedDamageDealt += enemies[i].TakeDamage(finalDmg, context.Caster);
            }

            if (totalUnblockedDamageDealt > 0 && context.Caster != null)
                context.Caster.Heal(totalUnblockedDamageDealt);
        }

        private int GetRawDamage(CardInstance card)
            => SelectValue(card, BaseDamage, UpgradedDamage, card != null ? card.BonusDamage : 0);

        public override string FormatDescription(string currentText, CombatContext context, CardInstance card)
            => ReplaceDamagePlaceholder(currentText, PlaceholderTag, GetRawDamage(card), context);

        public override string GetAssignedPlaceholderTag() => string.IsNullOrEmpty(PlaceholderTag) ? "{D}" : PlaceholderTag;
    }

    /// <summary>
    /// [특수 기믹 블록 3] 악마의 불꽃(FiendFire): 손패의 모든 카드를 원자적으로 소멸시키고 그 장수만큼 시간차 타격합니다.
    /// </summary>
    [Serializable]
    public class ExhaustHandDamageEffect : CardEffectBase
    {
        public int BaseDamagePerCard = 7;
        public int UpgradedDamagePerCard = 10;
        public float HitInterval = 0.08f;
        public string PlaceholderTag = "{D}";

        public override void Execute(CombatContext context, CardInstance card)
        {
            if (!TryBeginExhaustHand(context, card, out int rawDmg, out int exhaustedCount)) return;

            for (int i = 0; i < exhaustedCount; i++)
            {
                if (!PerformExhaustHit(context, rawDmg)) break;
            }
        }

        public override IEnumerator ExecuteRoutine(CombatContext context, CardInstance card)
        {
            if (!TryBeginExhaustHand(context, card, out int rawDmg, out int exhaustedCount)) yield break;

            for (int i = 0; i < exhaustedCount; i++)
            {
                if (!PerformExhaustHit(context, rawDmg)) yield break;

                if (i < exhaustedCount - 1 && HitInterval > 0f)
                    yield return new WaitForSeconds(HitInterval);
            }
        }

        private int GetRawDamage(CardInstance card)
            => SelectValue(card, BaseDamagePerCard, UpgradedDamagePerCard, card != null ? card.BonusDamage : 0);

        private bool TryBeginExhaustHand(CombatContext context, CardInstance card, out int rawDmg, out int exhaustedCount)
        {
            rawDmg = GetRawDamage(card);
            exhaustedCount = 0;
            if (context.DeckOps == null || context.PrimaryTarget == null) return false;

            CardExecutor.BeginSelfExhaustHandGuard(card);
            try
            {
                exhaustedCount = context.DeckOps.ExhaustEntireHand();
            }
            finally
            {
                // BattleFlow처럼 시전 카드가 아직 손패에 남아있던 상태에서 ExhaustEntireHand()가 실행된 경우
                // 자기 자신(1장)은 타격 횟수에서 제외
                if (CardExecutor.EndSelfExhaustHandGuard())
                    exhaustedCount = Mathf.Max(0, exhaustedCount - 1);
            }

            return exhaustedCount > 0;
        }

        private static bool PerformExhaustHit(CombatContext context, int rawDmg)
        {
            if (context.PrimaryTarget == null || !context.PrimaryTarget.IsAlive) return false;
            int finalDmg = DamageCalculator.CalculateDamage(rawDmg, context.Caster, context.PrimaryTarget, ignoreStunCheck: true);
            context.PrimaryTarget.TakeDamage(finalDmg, context.Caster);
            return true;
        }

        public override string FormatDescription(string currentText, CombatContext context, CardInstance card)
            => ReplaceDamagePlaceholder(currentText, PlaceholderTag, GetRawDamage(card), context);

        public override string GetAssignedPlaceholderTag() => string.IsNullOrEmpty(PlaceholderTag) ? "{D}" : PlaceholderTag;
    }

    /// <summary>
    /// [특수 기믹 블록 4] 참호(Entrench): 현재 방어도를 2배로 증폭합니다.
    /// </summary>
    [Serializable]
    public class MultiplyBlockEffect : CardEffectBase
    {
        public int Multiplier = 2;

        public override void Execute(CombatContext context, CardInstance card)
        {
            context.Caster?.MultiplyBlock(Multiplier);
        }
    }

    /// <summary>
    /// [특수 기믹 블록 5] 약점 포착(SpotWeakness): 대상 적이 공격 의도(Intent)일 때만 힘을 획득합니다.
    /// </summary>
    [Serializable]
    public class SpotWeaknessEffect : CardEffectBase
    {
        public int BaseStrengthGain = 3;
        public int UpgradedStrengthGain = 4;
        public string PlaceholderTag = "{M}";

        public override void Execute(CombatContext context, CardInstance card)
        {
            if (context.Caster == null || context.PrimaryTarget == null) return;

            bool intendsToAttack = context.IsTargetIntendingToAttack != null
                && context.IsTargetIntendingToAttack.Invoke(context.PrimaryTarget);

            if (intendsToAttack)
                context.Caster.AddStatus(StatusKeys.Strength, SelectValue(card, BaseStrengthGain, UpgradedStrengthGain));
        }

        public override string FormatDescription(string currentText, CombatContext context, CardInstance card)
            => ReplaceValuePlaceholder(currentText, PlaceholderTag, "{M}", SelectValue(card, BaseStrengthGain, UpgradedStrengthGain));

        public override string GetAssignedPlaceholderTag() => string.IsNullOrEmpty(PlaceholderTag) ? "{M}" : PlaceholderTag;
    }

    /// <summary>
    /// [기본 블록 5] 체력 회복 또는 체력 소모(자해 코스트) 효과
    /// • 양수(+4)면 회복(Heal), 음수(-2, -6)면 방어도를 무시하는 체력 소모(TakeDirectDamage)로 작동합니다.
    /// • 설명 텍스트의 {H} 태그를 수치(절댓값)로 자동 치환합니다.
    /// </summary>
    [Serializable]
    public class ModifyHPEffect : CardEffectBase
    {
        [Tooltip("양수면 체력 회복, 음수면 방어도 무시 체력 소모(예: 혈투 -2, 봉헌 -6)")]
        public int BaseHPDelta = -2;
        public int UpgradedHPDelta = -2;
        public ECardTarget ApplyTo = ECardTarget.Self;
        [Tooltip("카드 설명에서 치환할 태그 (기본: {H})")]
        public string PlaceholderTag = "{H}";

        public override void Execute(CombatContext context, CardInstance card)
        {
            int delta = SelectValue(card, BaseHPDelta, UpgradedHPDelta);
            if (delta == 0) return;

            var targets = context.GetValidTargets(ApplyTo, TargetBuffer);
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target == null || !target.IsAlive) continue;

                if (delta > 0)
                    target.Heal(delta);
                else
                    target.TakeDirectDamage(-delta, context.Caster);
            }
        }

        public override string FormatDescription(string currentText, CombatContext context, CardInstance card)
            => ReplaceValuePlaceholder(currentText, PlaceholderTag, "{H}", Mathf.Abs(SelectValue(card, BaseHPDelta, UpgradedHPDelta)));

        public override string GetAssignedPlaceholderTag() => string.IsNullOrEmpty(PlaceholderTag) ? "{H}" : PlaceholderTag;
    }

    /// <summary>
    /// [특수 기믹 블록 6] 무기 제련 / 무장(Armaments): 전투 중 현재 손패의 카드를 강화합니다.
    /// </summary>
    [Serializable]
    public class UpgradeHandEffect : CardEffectBase
    {
        [Tooltip("기본 상태에서 손패 전체를 강화할지 여부 (false면 1장 강화)")]
        public bool BaseUpgradeAll = false;
        [Tooltip("강화(Card+) 상태에서 손패 전체를 강화할지 여부")]
        public bool UpgradedUpgradeAll = true;

        public override void Execute(CombatContext context, CardInstance card)
        {
            if (context.DeckOps == null) return;
            bool isUpgraded = card != null && card.IsUpgraded;
            bool upgradeAll = isUpgraded ? UpgradedUpgradeAll : BaseUpgradeAll;

            // BattleFlow처럼 시전 중인 카드가 아직 손패 목록에 남아있는 경우,
            // 자기 자신이 강화 후보로 뽑히지 않도록 잠시 후보 제외 플래그 설정
            if (card != null) card.SuppressHandUpgradeCandidate = true;
            try
            {
                context.DeckOps.UpgradeCardsInHand(upgradeAll);
            }
            finally
            {
                if (card != null) card.SuppressHandUpgradeCandidate = false;
            }
        }
    }
}
