using System;
using System.Collections.Generic;
using UnityEngine;

namespace CNF.Data
{
    /// <summary>
    /// 언리얼 FPotionDataRow를 대체하는 순수 물약(Potion) 데이터 컨테이너.
    /// 물약 담당 팀원이 DataManager.Instance.GetPotion(id)로 조회하여 사용합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "Potion_", menuName = "CNF/Data/PotionDataSO")]
    public class PotionDataSO : CustomParamContainerSO
    {
        [Header("1. 기본 정보")]
        public string PotionID;
        public string PotionName;
        [TextArea(2, 4)] public string Description;
        public Sprite Icon;
        public Color LiquidColor = Color.red;
        public EPotionRarity Rarity = EPotionRarity.Common;
        public int ShopPrice = 50;

        [Header("2. 타겟 및 효과 수치 데이터")]
        public ECardTarget TargetType = ECardTarget.Self;
        public EPotionEffectType EffectType = EPotionEffectType.Heal;
        public int EffectValue = 20;      // 회복량, 피해량, 방어도, 부활 체력 비율(%) 등
        [StatusIdSelector]
        public string StatusID;           // 버프/디버프 물약일 때 부여할 StatusID
        public int StatusStacks = 0;

        [Header("3. 연출 데이터 (선택)")]
        public AudioClip UseSFX;
        public GameObject ThrowVFXPrefab;
    }
}
