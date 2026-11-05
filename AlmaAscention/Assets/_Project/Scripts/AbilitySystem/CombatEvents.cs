using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using CNF.Data;

namespace CNF.AbilitySystem
{
    /// <summary>
    /// 유물(Relic), 파워(Power), UI 연출, 사운드 담당 팀원이 어디서든 구독할 수 있는 전역 전투 이벤트 허브.
    /// [메모리 누수 자동 방어]: 씬 전환 시 파괴된(Destroyed) UnityEngine.Object 구독자를 자동으로 정리(Prune)합니다.
    /// </summary>
    public static class CombatEvents
    {
        /// <summary>카드 시전 완료 시: (사용된 카드, 전투 문맥, 소멸 여부)</summary>
        public static event Action<CardInstance, CombatContext, bool> OnCardPlayed;

        /// <summary>카드 소멸(Exhaust) 시: (소멸된 카드, 소유 캐릭터)</summary>
        public static event Action<CardInstance, StatusController> OnCardExhausted;

        /// <summary>피해 발생 시: (피격자, 공격자[없으면 null], 방어도에 막힌 피해량, 실제 깎인 HP 피해량)</summary>
        public static event Action<StatusController, StatusController, int, int> OnDamageTaken;

        /// <summary>방어도 획득 시: (대상, 획득한 방어도량, 현재 총 방어도)</summary>
        public static event Action<StatusController, int, int> OnBlockGained;

        /// <summary>상태이상 변동 시: (대상, StatusID, 변경량, 현재 최종 스택)</summary>
        public static event Action<StatusController, string, int, int> OnStatusApplied;

        /// <summary>체력 회복 시: (대상, 회복량, 현재 HP)</summary>
        public static event Action<StatusController, int, int> OnHealed;

        /// <summary>캐릭터/몬스터 사망 시: (사망자, 처치자[없으면 null])</summary>
        public static event Action<StatusController, StatusController> OnCharacterDied;

        /// <summary>턴 시작 처리 시: (대상 캐릭터)</summary>
        public static event Action<StatusController> OnTurnStarted;

        /// <summary>턴 종료 처리 시: (대상 캐릭터)</summary>
        public static event Action<StatusController> OnTurnEnded;

        /// <summary>마나 소모 시: (시전자, 소모한 마나량, 남은 마나량)</summary>
        public static event Action<StatusController, int, int> OnManaSpent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeOnLoad()
        {
            ClearAllSubscriptions();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            PruneDeadSubscribers();
        }

        public static void ClearAllSubscriptions()
        {
            OnCardPlayed = null;
            OnCardExhausted = null;
            OnDamageTaken = null;
            OnBlockGained = null;
            OnStatusApplied = null;
            OnHealed = null;
            OnCharacterDied = null;
            OnTurnStarted = null;
            OnTurnEnded = null;
            OnManaSpent = null;
        }

        public static void PruneDeadSubscribers()
        {
            OnCardPlayed = PruneDelegate(OnCardPlayed);
            OnCardExhausted = PruneDelegate(OnCardExhausted);
            OnDamageTaken = PruneDelegate(OnDamageTaken);
            OnBlockGained = PruneDelegate(OnBlockGained);
            OnStatusApplied = PruneDelegate(OnStatusApplied);
            OnHealed = PruneDelegate(OnHealed);
            OnCharacterDied = PruneDelegate(OnCharacterDied);
            OnTurnStarted = PruneDelegate(OnTurnStarted);
            OnTurnEnded = PruneDelegate(OnTurnEnded);
            OnManaSpent = PruneDelegate(OnManaSpent);
        }

        private static T PruneDelegate<T>(T del) where T : Delegate
        {
            if (del == null) return null;
            Delegate[] invocationList = del.GetInvocationList();
            Delegate result = null;

            for (int i = 0; i < invocationList.Length; i++)
            {
                Delegate d = invocationList[i];
                if (d.Target is UnityEngine.Object unityObj && unityObj == null)
                    continue;

                result = Delegate.Combine(result, d);
            }
            return (T)result;
        }

        internal static void RaiseCardPlayed(CardInstance card, CombatContext ctx, bool exhausted)
            => OnCardPlayed?.Invoke(card, ctx, exhausted);

        public static void RaiseCardExhausted(CardInstance card, StatusController owner)
            => OnCardExhausted?.Invoke(card, owner);

        internal static void RaiseDamageTaken(StatusController target, StatusController attacker, int blockedDmg, int lostHP)
            => OnDamageTaken?.Invoke(target, attacker, blockedDmg, lostHP);

        internal static void RaiseBlockGained(StatusController target, int gained, int totalBlock)
            => OnBlockGained?.Invoke(target, gained, totalBlock);

        internal static void RaiseStatusApplied(StatusController target, string statusId, int added, int currentStacks)
            => OnStatusApplied?.Invoke(target, statusId, added, currentStacks);

        internal static void RaiseHealed(StatusController target, int amount, int currentHP)
            => OnHealed?.Invoke(target, amount, currentHP);

        internal static void RaiseCharacterDied(StatusController victim, StatusController killer = null)
            => OnCharacterDied?.Invoke(victim, killer);

        internal static void RaiseTurnStarted(StatusController owner)
            => OnTurnStarted?.Invoke(owner);

        internal static void RaiseTurnEnded(StatusController owner)
            => OnTurnEnded?.Invoke(owner);

        internal static void RaiseManaSpent(StatusController owner, int spent, int remaining)
            => OnManaSpent?.Invoke(owner, spent, remaining);
    }
}
