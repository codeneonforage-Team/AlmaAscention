using System;
using UnityEngine;
using CNF.Data;
using CNF.AbilitySystem;

namespace AlmaAscention.Combat
{
    // StatusController랑 EnemyBrain 연결해주는 몬스터 1체짜리 전투 런타임 컴포넌트임.
    // 현재 의도/페이즈/행동 기회 번호/쿨다운/직전 패턴 이런 건 다 EnemyBrain이 들고 있고,
    // 여긴 그 상태를 StatusController랑 UI(EnemyIntentView)에 이어주는 얇은 어댑터일 뿐임.
    // HP는 StatusController가 유일한 소스라 여기서 따로 안 들고 있음.
    [RequireComponent(typeof(StatusController))]
    public class EnemyRuntime : MonoBehaviour
    {
        public MonsterDataSO Data { get; private set; }
        public StatusController Status { get; private set; }
        public MonsterSkillPatternData CurrentIntent { get; private set; }

        private EnemyBrain _brain;
        private StatusController _previewTarget;

        // 의도가 (다시) 선택될 때마다 발생함. UI는 이 이벤트로만 다음 의도 받아가야 하고, 자기가 알아서 재추첨 걸면 안 됨.
        public event Action<EnemyRuntime> OnIntentChanged;

        public MonsterPhaseData CurrentPhase => _brain?.CurrentPhase;
        public int ActionOpportunityNumber => _brain?.ActionOpportunityNumber ?? 0;
        public string LastPatternID => _brain?.LastPatternID;
        public int ConsecutiveUseCount => _brain?.ConsecutiveUseCount ?? 0;

        public void Initialize(MonsterDataSO data, StatusController status, StatusController previewTarget, System.Random random)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (status == null) throw new ArgumentNullException(nameof(status));

            Data = data;
            Status = status;
            _previewTarget = previewTarget;
            _brain = new EnemyBrain(data, random);
            CurrentIntent = null;
        }

        public int GetCooldownRemaining(string patternId) => _brain?.GetCooldownRemaining(patternId) ?? 0;

        // 현재 체력 비율로 다음 의도 고르고 예고(announce)함.
        // 전투 시작하고 첫 의도 뽑을 때랑, 매 행동 끝나고 다음 의도 뽑을 때 둘 다 이거 씀.
        public void SelectAndAnnounceNextIntent()
        {
            if (_brain == null || Status == null) return;

            float ratio = Status.MaxHP > 0 ? (float)Status.CurrentHP / Status.MaxHP : 0f;
            CurrentIntent = _brain.SelectNextPattern(ratio);
            OnIntentChanged?.Invoke(this);
        }

        public bool IsIntentAttacking()
        {
            if (CurrentIntent == null) return false;
            switch (CurrentIntent.IntentType)
            {
                case EMonsterIntentType.Attack:
                case EMonsterIntentType.MultiAttack:
                case EMonsterIntentType.AttackAndDefend:
                case EMonsterIntentType.AttackAndDebuff:
                    return true;
                default:
                    return false;
            }
        }

        // 지금 예고된 의도, 한 타당 최종 예상 피해임 (공격자/대상 상태이상 반영됨).
        public int GetPreviewDamagePerHit()
        {
            if (CurrentIntent == null || CurrentIntent.BaseDamage <= 0 || Status == null) return 0;
            return DamageCalculator.CalculateDamage(CurrentIntent.BaseDamage, Status, _previewTarget, ignoreStunCheck: true);
        }

        public int GetPreviewHitCount() => CurrentIntent != null ? Mathf.Max(0, CurrentIntent.HitCount) : 0;

        // 지금 예고된 의도의 예상 방어도 획득량 (민첩/손상 반영됨).
        public int GetPreviewBlock()
        {
            if (CurrentIntent == null || CurrentIntent.BlockGain <= 0 || Status == null) return 0;
            return DamageCalculator.CalculatePreviewBlock(CurrentIntent.BlockGain, Status);
        }

        private void OnDestroy()
        {
            OnIntentChanged = null;
        }
    }
}
