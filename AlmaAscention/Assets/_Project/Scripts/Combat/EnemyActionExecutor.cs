using System;
using System.Collections;
using UnityEngine;
using CNF.Data;
using CNF.AbilitySystem;

namespace AlmaAscention.Combat
{
    // 몬스터 패턴(MonsterSkillPatternData) 공용으로 돌리는 정적 실행기임.
    // 몬스터 이름/ID별로 if문 분기 치는 거 없이, 패턴에 적힌 수치(BaseDamage/HitCount/BlockGain/InflictStatusID 등)만 보고
    // 공격·방어·상태이상 처리함. 새 몬스터/보스는 데이터만 추가하면 이거로 그냥 돌아감.
    public static class EnemyActionExecutor
    {
        // 패턴 실행하는 함수. 다단 공격이면 타격마다 공격자/대상 살아있는지, 전투 끝났는지 매번 다시 확인하고,
        // 가시(Thorns) 반사로 공격자 죽으면 남은 타격 바로 중단함.
        // 애니메이션 이벤트 없어도 끝나도록, 연출 대기는 호출한 쪽에서 넘긴 hitInterval만 씀 (외부 콜백 대기 따로 없음).
        public static IEnumerator ExecutePatternRoutine(
            StatusController actor,
            StatusController target,
            MonsterSkillPatternData pattern,
            Func<bool> isBattleOver,
            Action<string> onLog,
            float hitInterval = 0.12f)
        {
            if (pattern == null || actor == null || !actor.IsAlive)
                yield break;

            if (isBattleOver == null) isBattleOver = () => false;

            int hits = Mathf.Max(0, pattern.HitCount);
            if (pattern.BaseDamage > 0 && hits > 0 && target != null)
            {
                for (int hit = 0; hit < hits; hit++)
                {
                    if (isBattleOver()) yield break;
                    if (!actor.IsAlive || !target.IsAlive) yield break;

                    int finalDamage = DamageCalculator.CalculateDamage(pattern.BaseDamage, actor, target, ignoreStunCheck: true);
                    int lostHP = target.TakeDamage(finalDamage, actor);
                    onLog?.Invoke($"{DescribeName(actor)}의 [{pattern.PatternName}] -> {DescribeName(target)}에게 {finalDamage} 피해 (실제 HP 손실 {lostHP})");

                    if (isBattleOver()) yield break;
                    if (!actor.IsAlive)
                    {
                        onLog?.Invoke($"{DescribeName(actor)}가 반사 피해로 사망하여 남은 타격을 중단합니다.");
                        yield break;
                    }

                    if (hit < hits - 1 && hitInterval > 0f)
                        yield return new WaitForSeconds(hitInterval);
                }
            }

            if (isBattleOver() || !actor.IsAlive) yield break;

            if (pattern.BlockGain > 0)
            {
                actor.AddBlock(pattern.BlockGain, applyDexterity: true);
                onLog?.Invoke($"{DescribeName(actor)}가 [{pattern.PatternName}]으로 방어도 {pattern.BlockGain}(원본)을 얻습니다.");
            }

            if (!string.IsNullOrEmpty(pattern.InflictStatusID) && pattern.StatusStacks != 0)
            {
                StatusController statusTarget = pattern.ApplyStatusToSelf ? actor : target;
                if (statusTarget != null && statusTarget.IsAlive)
                {
                    statusTarget.AddStatus(pattern.InflictStatusID, pattern.StatusStacks);
                    onLog?.Invoke($"{DescribeName(actor)}가 [{pattern.PatternName}]으로 {DescribeName(statusTarget)}에게 {pattern.InflictStatusID} {pattern.StatusStacks}을 부여합니다.");
                }
            }

            if (!string.IsNullOrEmpty(pattern.SummonMonsterID))
            {
                onLog?.Invoke($"[미구현] {DescribeName(actor)}의 [{pattern.PatternName}] 소환 요청 (SummonMonsterID={pattern.SummonMonsterID})은 이 샌드박스 범위 밖입니다.");
            }
        }

        private static string DescribeName(StatusController c)
        {
            if (c == null) return "(알수없음)";
            return c.name;
        }
    }
}
