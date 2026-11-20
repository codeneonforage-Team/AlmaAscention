using System;
using UnityEngine;

namespace CNF.Data
{
    /// <summary>
    /// string 타입의 StatusID 필드에 붙이면 유니티 인스펙터에서 오타 방지용 드롭다운 목록으로 그려주는 어트리뷰트.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class StatusIdSelectorAttribute : PropertyAttribute { }

    public enum ECardType
    {
        Attack,
        Skill,
        Power,
        Status,
        Curse
    }

    public enum ECardTarget
    {
        SingleEnemy,
        AllEnemies,
        RandomEnemy,
        Self,
        None
    }

    public enum ECardRarity
    {
        Starter,
        Common,
        Uncommon,
        Rare,
        Special,
        Curse
    }

    public enum EStatusPolarity
    {
        Buff,
        Debuff,
        Neutral
    }

    public enum ERelicRarity
    {
        Starter,
        Common,
        Uncommon,
        Rare,
        Boss,
        Shop,
        Event,
        Fusion
    }

    public enum ERelicTriggerType
    {
        Passive,
        OnAcquire,
        OnBattleStart,
        OnTurnStart,
        OnTurnEnd,
        OnCardPlayed,
        OnCardExhausted,
        OnDamaged,
        OnBattleWin,
        OnDeckReshuffled,
        OnEnemyKilled,
        OnManaDepleted,
        OnHandEmptied
    }

    public enum EPotionRarity
    {
        Common,
        Uncommon,
        Rare
    }

    public enum EPotionEffectType
    {
        Heal,
        Block,
        BuffStrength,
        BuffDexterity,
        DamageSingle,
        DamageAll,
        DebuffWeak,
        DebuffVulnerable,
        DebuffPoison,
        DrawCards,
        GainMana,
        Resurrect,
        Custom
    }

    public enum EMonsterGrade
    {
        Normal,
        Elite,
        Boss
    }

    public enum EMonsterIntentType
    {
        Attack,
        MultiAttack,
        Defend,
        AttackAndDefend,
        Buff,
        Debuff,
        AttackAndDebuff,
        Summon,
        Stunned,
        Unknown
    }

    /// <summary>
    /// 기본 내장 상태이상/파워 키 상수.
    /// </summary>
    public static class StatusKeys
    {
        public const string Strength    = "Strength";    // 힘 (공격 피해 증감, 음수 가능)
        public const string Dexterity   = "Dexterity";   // 민첩 (방어도 획득 증감, 음수 가능)
        public const string Vulnerable  = "Vulnerable";  // 취약 (받는 피해 50% 증가)
        public const string Weak        = "Weak";        // 약화 (주는 피해 25% 감소)
        public const string Frail       = "Frail";       // 손상 (카드로 얻는 방어도 25% 감소)
        public const string Poison      = "Poison";      // 중독 (턴 종료 시 고정 피해 및 1 감소)
        public const string Burn        = "Burn";        // 화상 (턴 종료 시 고정 피해 및 1 감소)
        public const string Mark        = "Mark";        // 표식
        public const string Shock       = "Shock";       // 감전 (4스택 도달 시 번개 폭발 + 기절 1스택)
        public const string Stunned     = "Stunned";     // 기절 (공격 의도 0 및 턴 스킵)

        // 기본 내장 파워/특수 버프 키
        public const string Artifact    = "Artifact";    // 인공물 (디버프 부여 시 1스택 소모하여 무효화)
        public const string Thorns      = "Thorns";      // 가시 (피격 시 공격자에게 스택만큼 반사 피해)
        public const string Barricade   = "Barricade";   // 바리케이드 (턴 시작 시 방어도가 사라지지 않음)
        public const string Blur        = "Blur";        // 잔상 (다음 턴 시작 시 방어도 유지 후 1스택 감소)
        public const string Metallicize = "Metallicize"; // 금속화 (턴 종료 시 스택만큼 방어도 획득)
        public const string Ritual      = "Ritual";      // 의식 (턴 종료 시 스택만큼 힘 획득)
        public const string FeelNoPain  = "FeelNoPain";  // 무감각 (카드 소멸 시 스택만큼 방어도 획득)
        public const string DarkEmbrace = "DarkEmbrace"; // 어둠의 포옹 (카드 소멸 시 스택만큼 드로우)
        public const string LoseStrengthAtTurnEnd  = "LoseStrengthAtTurnEnd";  // 임시 힘 만료 (턴 종료 시 스택만큼 힘 감소)
        public const string LoseDexterityAtTurnEnd = "LoseDexterityAtTurnEnd"; // 임시 민첩 만료 (턴 종료 시 스택만큼 민첩 감소)

        // 신규 유물 기획(10~35번) 연동용 특수 상태이상/패시브 키
        public const string GrowingPoison            = "GrowingPoison";            // 독을 머금은 단검(19번): 턴 종료 시 고정 피해 후 스택 1 감소 대신 1 증가
        public const string VulnerableDoubleDamage   = "VulnerableDoubleDamage";   // 용혈 귀걸이(17번): 취약 상태 적에게 주는 피해 배율 1.5배 -> 2.0배
        public const string FullHpDoubleDamage       = "FullHpDoubleDamage";       // 고기 호빵(21번): 공격자 풀피(CurrentHP == MaxHP) 유지 시 공격 피해 2배
        public const string RepeatIntentDoubleDamage = "RepeatIntentDoubleDamage"; // 재액의 단안경(14번): 적이 이미 했던 행동을 반복 중일 때 공격 피해 2배
        public const string RepeatActionMark         = "RepeatActionMark";         // 재액의 단안경(14번): 현재 중복 행동을 예고한 적 개체에게 부여되는 표식
        public const string PoisonTargetTripleDamage = "PoisonTargetTripleDamage"; // 녹슨 단검(23번): 독 상태의 적에게 주는 피해 3배
        public const string DispelTargetOnAttack     = "DispelTargetOnAttack";     // 만념단모(29번): 공격 적중 시 대상의 모든 버프/디버프/회피 상태 해제
        public const string InflictWeakVulnOnAttack  = "InflictWeakVulnOnAttack";  // 퀴클로노미콘(30번): 공격 적중 시 대상에게 취약과 약화 부여
        public const string KyklonomiconCurse        = "KyklonomiconCurse";        // 퀴클로노미콘(30번): 4턴 내 미처치 시 턴 종료마다 최대체력 10 감소 카운터

        // 추후 세부 효과 확정 시 StatusBehaviorSO 또는 StatusController에 연결할 예약 상태이상 키
        public const string Whirlwind                = "Whirlwind";                // 금으로 도금된 클로(35번): [회오리] 상태 (예약 키)
        public const string Evasion                  = "Evasion";                  // 만념단모(29번) 등에서 참조하는 [회피] 상태 (예약 키)

        public static readonly string[] AllBuiltInKeys = new[]
        {
            Strength, Dexterity, Vulnerable, Weak, Frail, Poison, Burn, Mark, Shock, Stunned,
            Artifact, Thorns, Barricade, Blur, Metallicize, Ritual, FeelNoPain, DarkEmbrace,
            LoseStrengthAtTurnEnd, LoseDexterityAtTurnEnd,
            GrowingPoison, VulnerableDoubleDamage, FullHpDoubleDamage, RepeatIntentDoubleDamage, RepeatActionMark,
            PoisonTargetTripleDamage, DispelTargetOnAttack, InflictWeakVulnOnAttack, KyklonomiconCurse,
            Whirlwind, Evasion
        };

        /// <summary>
        /// 힘(Strength), 민첩(Dexterity)처럼 0 미만(음수 디버프)으로 내려갈 수 있는 상태이상인지 판별합니다.
        /// </summary>
        public static bool IsNegativeStackAllowed(string statusId)
        {
            return string.Equals(statusId, Strength, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, Dexterity, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 인공물(Artifact)로 방어 가능한 디버프 부여인지 판별합니다.
        /// </summary>
        public static bool IsBuiltInDebuff(string statusId, int addedAmount)
        {
            if (string.IsNullOrEmpty(statusId)) return false;

            if (IsNegativeStackAllowed(statusId))
                return addedAmount < 0;

            return string.Equals(statusId, Vulnerable, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, Weak, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, Frail, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, Poison, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, GrowingPoison, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, Burn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, Mark, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, Shock, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, Stunned, StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusId, Whirlwind, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 기본 내장 마법부여(인챈트) 키 상수.
    /// </summary>
    public static class EnchantmentKeys
    {
        public const string None         = "";
        public const string Vitality     = "Vitality";     // 기혈
        public const string SoulAwaken   = "SoulAwaken";   // 영혼 각성
        public const string DeadlyPoison = "DeadlyPoison"; // 맹독
    }
}
