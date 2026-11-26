using CNF.AbilitySystem;

namespace AlmaAscention.Relic
{
    // 전투 진행 코드가 호출하는 유물 진입점. 호출 순서는 호출자가 관리한다.
    public interface IRelicBattleHooks
    {
        // 플레이어 턴의 행동치 충전·드로우 후 한 번 호출한다.
        // 획득 이후 누적 턴 증가 및 턴 시작 유물 발동을 처리한다.
        int OnTurnStart(CombatContext context);

        // 플레이어 턴 종료 시 손패 버림·소멸 처리 전에 한 번 호출한다.
        int OnTurnEnd(CombatContext context);

        // 플레이어의 카드 사용 성공마다 한 번 호출한다. 기존 TriggerRelics 호출과 중복하지 않는다.
        int OnCardPlayed(CombatContext context);

        // 적 한 개체의 사망마다 한 번 호출한다. 대상 선별은 호출자가 담당한다.
        int OnEnemyKilled(CombatContext context);

        // 승패와 관계없이 전투 종료 시 한 번 호출한다.
        // 승리 유물 발동은 호출자가 이 메서드 전에 별도로 요청한다.
        // 누적 플레이어 턴은 유지하고 전투 횟수 쿨타임을 갱신한다.
        void OnBattleEnd(CombatContext context);
    }
}
