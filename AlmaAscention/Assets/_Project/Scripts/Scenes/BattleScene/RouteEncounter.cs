using CNF.Data;

public static class RouteEncounter
{
    public static MonsterDataSO Resolve(RouteNode node, int stage)
    {
        if (node.Type != RouteNodeType.Battle && node.Type != RouteNodeType.Boss)
            return null;

        return DataManager.Instance?.GetMonster($"{node.Type}_{stage}_{node.Variant}");
    }

    public static string Preview(MonsterDataSO monster)
    {
        var phases = monster.GetEffectivePhases();
        if (phases.Count != 1 || phases[0]?.Patterns?.Count != 1 || phases[0].Patterns[0] == null)
            return monster.MonsterName;

        string style = phases[0].Patterns[0].IntentType switch
        {
            EMonsterIntentType.Attack => "강한 단일 공격",
            EMonsterIntentType.MultiAttack => "연속 공격",
            EMonsterIntentType.AttackAndDefend => "공격과 방어",
            _ => phases[0].Patterns[0].PatternName
        };
        return string.IsNullOrEmpty(style) ? monster.MonsterName : $"{monster.MonsterName} · {style}";
    }
}
