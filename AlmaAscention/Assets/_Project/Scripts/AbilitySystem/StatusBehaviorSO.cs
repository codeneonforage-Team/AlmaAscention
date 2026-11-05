using UnityEngine;
using CNF.Data;

namespace CNF.AbilitySystem
{
    /// <summary>
    /// 상태이상(버프/디버프/파워)의 동작을 모듈화하는 베이스 ScriptableObject.
    /// 향후 신규 상태이상이 기획되면 StatusController 코드를 수정할 필요 없이
    /// 이 클래스를 상속받아 새 에셋으로 등록하기만 하면 전투 연산과 턴 훅에 자동 편입됩니다.
    /// </summary>
    public abstract class StatusBehaviorSO : ScriptableObject
    {
        [Header("상태이상 메타데이터")]
        public string StatusID;
        public string DisplayName;
        [TextArea] public string DescriptionTemplate;
        public Sprite Icon;
        public EStatusPolarity Polarity = EStatusPolarity.Debuff;

        [Tooltip("전투 종료 시 자동 초기화 여부")]
        public bool ClearOnBattleEnd = true;

        [Tooltip("음수 스택 허용 여부 (예: 힘 -2, 민첩 -1 등)")]
        public bool AllowNegativeStacks = false;

        [Tooltip("수식 개입 우선순위 (낮을수록 먼저 계산. 예: 힘(합연산)=10, 약화/취약(곱연산)=50)")]
        public int CalculationPriority = 50;

        /// <summary>
        /// 스택이 추가되거나 변경된 직후 호출 (예: 감전 4스택 폭발 처리)
        /// </summary>
        public virtual void OnStackAdded(StatusController owner, ref int currentStacks, int addedAmount) { }

        /// <summary>
        /// 공격자가 피해를 줄 때 수식 개입 (예: 힘 +N, 약화 x0.75, 기절 시 0)
        /// </summary>
        public virtual void ModifyOutgoingDamage(StatusController attacker, StatusController target, ref float damage, int stacks) { }

        /// <summary>
        /// 방어자가 피해를 받을 때 수식 개입 (예: 취약 x1.5)
        /// </summary>
        public virtual void ModifyIncomingDamage(StatusController target, StatusController attacker, ref float damage, int stacks) { }

        /// <summary>
        /// 방어도(Block) 획득 시 수식 개입 (예: 민첩 +N)
        /// </summary>
        public virtual void ModifyBlockGain(StatusController owner, ref float block, int stacks) { }

        /// <summary>
        /// 턴 시작 시 발동 훅
        /// </summary>
        public virtual void OnTurnStart(StatusController owner, ref int currentStacks) { }

        /// <summary>
        /// 턴 종료 시 발동 훅 (예: 중독/화상 피해, 약화/취약 1턴 차감)
        /// </summary>
        public virtual void OnTurnEnd(StatusController owner, ref int currentStacks) { }
    }

    /// <summary>
    /// 카드 마법부여(인챈트: 기혈, 영혼각성 등)를 모듈화하는 베이스 ScriptableObject.
    /// 신규 인챈트 추가 시 CardExecutor를 고치지 않고 새 SO만 추가하면 됩니다.
    /// </summary>
    [CreateAssetMenu(fileName = "Enchant_", menuName = "CNF/AbilitySystem/EnchantmentBehaviorSO")]
    public class EnchantmentBehaviorSO : ScriptableObject
    {
        public string EnchantmentID;
        public string DisplayName;
        [TextArea] public string Description;

        [SerializeReference]
        public System.Collections.Generic.List<ICardEffect> OnPlayEffects = new();

        public virtual void OnCardPlayed(CombatContext context, CardInstance card)
        {
            for (int i = 0; i < OnPlayEffects.Count; i++)
            {
                OnPlayEffects[i]?.Execute(context, card);
            }
        }
    }
}
