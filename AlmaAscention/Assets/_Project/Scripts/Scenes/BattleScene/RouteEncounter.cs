using CNF.Data;
using UnityEngine;

// The route preview and the spawned enemy use the same encounter variant.
public static class RouteEncounter
{
    private static readonly string[] Guardians = { "탑의 수문장", "쌍검 파수꾼", "철갑 파수꾼" };
    private static readonly string[] Bosses = { "황혼의 심판자", "쌍월의 기사", "철벽의 군주" };
    private static readonly string[] Styles = { "강한 단일 공격", "연속 공격", "공격과 방어" };

    public static string Name(RouteNode node) =>
        (node.Type == RouteNodeType.Boss ? Bosses : Guardians)[node.Variant];

    public static string Preview(RouteNode node) => $"{Name(node)} · {Styles[node.Variant]}";

    public static MonsterDataSO Create(RouteNode node, int stage)
    {
        bool boss = node.Type == RouteNodeType.Boss;
        var monster = ScriptableObject.CreateInstance<MonsterDataSO>();
        monster.MonsterID = $"{node.Type}_{stage}_{node.Variant}";
        monster.MonsterName = Name(node);
        monster.Grade = boss ? EMonsterGrade.Boss : EMonsterGrade.Normal;
        monster.StageAppearance = stage;
        monster.MinHP = monster.MaxHP = (boss ? 50 : 32) + (stage - 1) * 4;
        int damage = (boss ? 9 : 6) + stage - 1;
        var attack = new MonsterSkillPatternData
        {
            PatternID = "Strike",
            PatternName = "일격",
            BaseDamage = damage
        };
        if (node.Variant == 1)
        {
            attack.IntentType = EMonsterIntentType.MultiAttack;
            attack.PatternName = "쌍검";
            attack.HitCount = 2;
            attack.BaseDamage = (damage + 1) / 2;
        }
        else if (node.Variant == 2)
        {
            attack.IntentType = EMonsterIntentType.AttackAndDefend;
            attack.PatternName = "방패 강타";
            attack.BaseDamage = damage - 2;
            attack.BlockGain = boss ? 5 : 3;
        }
        monster.Patterns.Add(attack);
        return monster;
    }
}
