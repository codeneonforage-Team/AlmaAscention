using System;
using System.Collections.Generic;
using UnityEngine;

namespace CNF.Data
{
    /// <summary>
    /// 랜덤 이벤트의 각 선택지(버튼) 데이터 구조체.
    /// 이벤트 담당 팀원이 이 데이터를 읽어 선택 가능 조건과 보상/패널티를 처리합니다.
    /// </summary>
    [Serializable]
    public class EventChoiceData
    {
        public string ChoiceText;
        [TextArea(2, 3)] public string ResultDescription;

        [Header("선택 조건")]
        public int RequiredGold = 0;
        public string RequiredRelicID;

        [Header("수치 변동 (음수면 소모/피해, 양수면 획득/회복)")]
        public int HPDelta = 0;
        public int MaxHPDelta = 0;
        public int GoldDelta = 0;

        [Header("보상 지급 ID")]
        public string RewardCardID;
        public string RewardRelicID;
        public string RewardPotionID;
        public bool TriggerCardRewardDraft = false;

        [Header("모달 호출 플래그")]
        public bool OpenCardRemovalModal = false;
        public bool OpenCardUpgradeModal = false;
        public bool OpenCardEnchantModal = false;
    }

    /// <summary>
    /// 언리얼 FEventDataRow를 대체하는 순수 이벤트 데이터 컨테이너.
    /// </summary>
    [CreateAssetMenu(fileName = "Event_", menuName = "CNF/Data/EventDataSO")]
    public class EventDataSO : CustomParamContainerSO
    {
        [Header("1. 기본 정보")]
        public string EventID;
        public string EventTitle;
        [TextArea(3, 6)] public string EventBodyText;
        public Sprite EventIllustration;

        [Header("2. 출현 조건 및 권능(재선) 연동")]
        public int MinStage = 1;
        public int MaxStage = 3;
        [Tooltip("불리한(위험/패널티) 이벤트인지 여부. true면 '재선' 권능 보유 시 아무 대가 없이 통과할 수 있습니다.")]
        public bool IsUnfavorableEvent = false;

        [Header("3. 선택지 목록")]
        public List<EventChoiceData> Choices = new();
    }
}
