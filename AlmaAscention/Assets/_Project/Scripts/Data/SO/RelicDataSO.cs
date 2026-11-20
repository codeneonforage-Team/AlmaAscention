using System;
using System.Collections.Generic;
using UnityEngine;

namespace CNF.Data
{
    /// <summary>
    /// 언리얼 FRelicDataRow를 대체하는 순수 성유물(Relic) 데이터 컨테이너.
    /// 유물 담당 팀원이 DataManager.Instance.GetRelic(id)로 조회하여 사용합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "Relic_", menuName = "CNF/Data/RelicDataSO")]
    public class RelicDataSO : CustomParamContainerSO
    {
        [Header("1. 기본 정보")]
        public string RelicID;
        public string RelicName;
        [TextArea(2, 4)] public string Description;
        [TextArea(1, 3)] public string FlavorText;
        public Sprite Icon;
        public ERelicRarity Rarity = ERelicRarity.Common;
        public int ShopPrice = 150;

        [Header("2. 발동 조건 및 수치 데이터")]
        public ERelicTriggerType TriggerType = ERelicTriggerType.Passive;
        public string EffectKey;          // 예: "GainBlock", "ApplyWeakAll", "HealOnWin" 등
        public int EffectValue = 0;       // 주요 수치
        [StatusIdSelector]
        public string StatusID;           // 상태이상 부여 유물인 경우 대상 StatusID
        public int StatusStacks = 0;

        [Header("3. 획득 즉시 적용 보너스 (망고, 딸기 등)")]
        public int AcquireMaxHealthBonus = 0;
        public int AcquireHealBonus = 0;
        public int AcquireGoldBonus = 0;

        [Header("4. 시한부 유물 & 전투 쿨타임 & 성유물 합성(Fusion) 데이터")]
        public bool IsTimedRelic = false;
        public int TimedDurationFloors = 0;
        [Tooltip("한 번 발동한 뒤 다시 발동하기까지 필요한 전투 횟수 쿨타임 (0이면 쿨타임 없음, 예: 전기밥솥=6, 팔괘로장갑=12, 양칭저울=5)")]
        public int CooldownBattles = 0;
        public bool IsRecipeExclusive = false;          // 합성 전용 유물 여부 (일반 드랍 제외)
        public List<string> FusionIngredientIDs = new(); // 조합식이 있는 경우 재료 유물 ID 3개
    }
}
