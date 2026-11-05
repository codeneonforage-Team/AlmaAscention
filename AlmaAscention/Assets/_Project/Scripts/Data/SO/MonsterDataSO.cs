using System;
using System.Collections.Generic;
using UnityEngine;

namespace CNF.Data
{
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
        [Tooltip("특정 턴 주기마다 발동할 경우 설정 (0이면 무관)")]
        public int TurnModulo = 0;
        [Tooltip("체력 비율이 이 값 이하일 때만 활성화 (1.0이면 상시)")]
        [Range(0f, 1f)] public float MaxHPRatioCondition = 1.0f;

        [Header("연출 데이터")]
        public string AnimTriggerName = "Attack";
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

        [Header("4. AI 행동 패턴 목록")]
        public List<MonsterSkillPatternData> Patterns = new();
    }
}
