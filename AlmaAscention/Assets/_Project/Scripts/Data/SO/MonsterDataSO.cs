using System;
using System.Collections.Generic;
using UnityEngine;

namespace CNF.Data
{
    // 패턴 풀에서 다음 의도 뽑는 방식임. 일반 풀에만 적용되고, 특수 패턴은 그냥 항상 우선순위로 먼저 뽑음.
    public enum EPatternSelectionMode
    {
        Sequential,
        WeightedRandom
    }

    /// <summary>
    /// 몬스터의 개별 행동 패턴(Intent) 데이터 구조체.
    /// AI 담당 팀원이 이 데이터를 읽어 다음 턴 의도(SelectNextIntent)를 결정합니다.
    /// </summary>
    [Serializable]
    public class MonsterSkillPatternData
    {
        public string PatternID;
        public string PatternName;
        public EMonsterIntentType IntentType = EMonsterIntentType.Attack;

        [Header("행동 수치")]
        public int BaseDamage = 8;
        public int HitCount = 1;
        public int BlockGain = 0;
        [StatusIdSelector]
        public string InflictStatusID;
        public int StatusStacks = 0;
        public bool ApplyStatusToSelf = false;
        public string SummonMonsterID;

        [Header("AI 선택 조건 (언리얼 CNF_AIController 조건부 패턴 데이터)")]
        public float SelectionWeight = 1.0f;
        public int CooldownTurns = 0;
        [Tooltip("0이면 연속 사용 제한 없음, 1 이상이면 최대 연속 사용 횟수 제한")]
        public int MaxConsecutiveUses = 0;
        [Tooltip("특정 턴 주기마다 발동할 경우 설정 (0이면 무관). 예: 3이면 3·6·9번째 행동 기회에만 조건 통과.")]
        public int TurnModulo = 0;
        [Tooltip("체력 비율이 이 값 이하일 때만 활성화 (1.0이면 상시)")]
        [Range(0f, 1f)] public float MaxHPRatioCondition = 1.0f;

        [Header("특수 패턴 분류 (EnemyBrain)")]
        [Tooltip("true면 일반 랜덤/순차 풀이 아닌 '특수 패턴' 후보로 취급되어, 조건을 만족할 경우 우선순위에 따라 일반 풀보다 먼저 선택됩니다.")]
        public bool IsSpecialPattern = false;
        [Tooltip("조건을 만족하는 특수 패턴이 여러 개일 때, 값이 높을수록 먼저 선택됩니다.")]
        public int SpecialPriority = 0;

        [Header("연출 데이터")]
        public string AnimTriggerName = "Attack";
    }

    // 보스처럼 페이즈 여러 개 있는 몬스터용 패턴 묶음.
    [Serializable]
    public class MonsterPhaseData
    {
        public string PhaseID;
        public string PhaseName;
        [Tooltip("현재 체력 비율이 이 값 이하로 내려가면 이 페이즈로 전환됩니다. 0번 페이즈는 보통 1.0(상시)로 둡니다.")]
        [Range(0f, 1f)] public float HPRatioThreshold = 1.0f;
        public EPatternSelectionMode SelectionMode = EPatternSelectionMode.WeightedRandom;
        public List<MonsterSkillPatternData> Patterns = new();
    }

    /// <summary>
    /// 언리얼 FMonsterDataRow를 대체하는 순수 몬스터 데이터 컨테이너.
    /// </summary>
    [CreateAssetMenu(fileName = "Monster_", menuName = "CNF/Data/MonsterDataSO")]
    public class MonsterDataSO : CustomParamContainerSO
    {
        [Header("1. 기본 식별 정보")]
        public string MonsterID;
        public string MonsterName;
        public EMonsterGrade Grade = EMonsterGrade.Normal;
        public int StageAppearance = 1;

        [Header("2. 기본 스탯")]
        public int MinHP = 40;
        public int MaxHP = 46;
        public int InitialBlock = 0;

        [Header("3. 외형 및 프리팹 참조")]
        public GameObject CharacterPrefab;
        public Sprite PortraitOrSprite;

        [Header("4. AI 행동 패턴 목록 (페이즈 미사용 몬스터의 기본/호환 경로)")]
        public List<MonsterSkillPatternData> Patterns = new();

        [Header("5. 보스 페이즈 목록 (설정 시 Patterns 대신 사용됨)")]
        [Tooltip("비어 있으면 Patterns를 체력 비율 1.0짜리 단일 페이즈로 취급합니다 (기존 에셋 호환 경로).")]
        public List<MonsterPhaseData> Phases = new();

        // Phases 있으면 그거 그대로 쓰고, 없으면 기존 Patterns를 페이즈 하나로 감싸서 돌려줌.
        // (페이즈 없는 옛날 에셋 호환용임)
        public IReadOnlyList<MonsterPhaseData> GetEffectivePhases()
        {
            if (Phases != null && Phases.Count > 0)
                return Phases;

            return new List<MonsterPhaseData>
            {
                new MonsterPhaseData
                {
                    PhaseID = "Default",
                    PhaseName = MonsterName,
                    HPRatioThreshold = 1.0f,
                    SelectionMode = EPatternSelectionMode.WeightedRandom,
                    Patterns = Patterns ?? new List<MonsterSkillPatternData>()
                }
            };
        }
    }
}
