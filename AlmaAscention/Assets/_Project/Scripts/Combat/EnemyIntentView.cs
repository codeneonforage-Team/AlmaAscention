using System.Text;
using UnityEngine;
using UnityEngine.UI;
using CNF.Data;
using CNF.AbilitySystem;

namespace AlmaAscention.Combat
{
    // 몬스터 1체의 지금 예고 패턴명/예상 피해/타수/방어·상태 효과 보여주는 UI 바인더임.
    // StatusController 변경 이벤트랑 EnemyRuntime.OnIntentChanged만 구독해서 "표시"만 다시 계산하고,
    // 직접 다음 행동 뽑거나 EnemyBrain 부르는 건 안 함.
    public class EnemyIntentView : MonoBehaviour
    {
        public EnemyRuntime Runtime { get; private set; }

        public Text NameText;
        public Text HPText;
        public Text BlockText;
        public Text StatusText;
        public Text PhaseText;
        public Text ActionNumberText;
        public Text IntentText;

        private StatusController _subscribedStatus;

        public void Bind(EnemyRuntime runtime)
        {
            Unbind();
            Runtime = runtime;
            if (Runtime == null)
            {
                RefreshAll();
                return;
            }

            Runtime.OnIntentChanged += HandleIntentChanged;
            _subscribedStatus = Runtime.Status;
            if (_subscribedStatus != null)
            {
                _subscribedStatus.OnHPChanged += HandleStatsChanged;
                _subscribedStatus.OnBlockChanged += HandleBlockChanged;
                _subscribedStatus.OnAnyStatusChanged += HandleAnyStatusChanged;
            }

            RefreshAll();
        }

        public void Unbind()
        {
            if (Runtime != null)
                Runtime.OnIntentChanged -= HandleIntentChanged;
            if (_subscribedStatus != null)
            {
                _subscribedStatus.OnHPChanged -= HandleStatsChanged;
                _subscribedStatus.OnBlockChanged -= HandleBlockChanged;
                _subscribedStatus.OnAnyStatusChanged -= HandleAnyStatusChanged;
            }
            Runtime = null;
            _subscribedStatus = null;
        }

        private void HandleIntentChanged(EnemyRuntime r) => RefreshAll();
        private void HandleStatsChanged(int cur, int max) => RefreshAll();
        private void HandleBlockChanged(int block) => RefreshAll();
        private void HandleAnyStatusChanged() => RefreshAll();

        public void RefreshAll()
        {
            if (Runtime == null || Runtime.Status == null)
            {
                SetText(NameText, "(empty)");
                SetText(HPText, string.Empty);
                SetText(BlockText, string.Empty);
                SetText(StatusText, string.Empty);
                SetText(PhaseText, string.Empty);
                SetText(ActionNumberText, string.Empty);
                SetText(IntentText, string.Empty);
                return;
            }

            StatusController s = Runtime.Status;
            SetText(NameText, Runtime.Data != null ? Runtime.Data.MonsterName : s.name);

            if (!s.IsAlive)
            {
                SetText(HPText, "사망");
                SetText(BlockText, string.Empty);
                SetText(StatusText, string.Empty);
                SetText(PhaseText, string.Empty);
                SetText(ActionNumberText, string.Empty);
                SetText(IntentText, "-");
                return;
            }

            SetText(HPText, $"HP {s.CurrentHP}/{s.MaxHP}");
            SetText(BlockText, $"방어도 {s.Block}");
            SetText(StatusText, FormatStatuses(s));
            SetText(PhaseText, Runtime.CurrentPhase != null ? $"Phase: {Runtime.CurrentPhase.PhaseName}" : string.Empty);
            SetText(ActionNumberText, $"행동기회 #{Runtime.ActionOpportunityNumber}");
            SetText(IntentText, FormatIntent(Runtime));
        }

        private static string FormatIntent(EnemyRuntime r)
        {
            MonsterSkillPatternData intent = r.CurrentIntent;
            if (intent == null) return "(대기)";

            string text = intent.PatternName;
            int perHit = r.GetPreviewDamagePerHit();
            int hits = r.GetPreviewHitCount();
            if (perHit > 0 && hits > 0)
                text += hits > 1 ? $" - 피해 {perHit} x{hits}" : $" - 피해 {perHit}";

            int block = r.GetPreviewBlock();
            if (block > 0)
                text += $" - 방어도 +{block}";

            return text;
        }

        private static string FormatStatuses(StatusController s)
        {
            var all = s.GetAllStatuses();
            if (all.Count == 0) return "상태없음";

            StringBuilder sb = new StringBuilder();
            foreach (var kvp in all)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kvp.Key).Append(' ').Append(kvp.Value);
            }
            return sb.ToString();
        }

        private static void SetText(Text t, string value)
        {
            if (t != null) t.text = value;
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
