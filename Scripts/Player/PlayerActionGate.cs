using System.Collections.Generic;
using UnityEngine;
using Cond = PlayerCondition;

/// <summary>
/// 플레이어가 할 수 있는 행동. 새 행동을 추가하면 PlayerActionGate의 규칙 표에도 넣는다.
/// </summary>
public enum PlayerAction
{
    Jump,           // 점프
    Punch,          // 펀치
    Grab,           // 붙잡기
    Dive,           // 다이빙
    Pickup,         // 아이템 줍기
    PlaceBox,       // 들고 있는 상자 배치
    DropItem,       // 들고 있는 아이템 놓기/던지기
    UseTool,        // 들고 있는 도구 사용
    ToggleLight,    // 두꺼비집 조작
    ChangeSlot,     // 인벤토리 슬롯 전환
    ToggleGun,      // 페인트 총 꺼내기
    FirePaint,      // 페인트 발사
    SpawnTrash,     // 마피아 쓰레기 생성
    DragObject,     // 드래그 오브젝트 끌기
}

/// <summary>
/// 행동 가능 여부를 좌우하는 플레이어의 현재 상태.
/// </summary>
[System.Flags]
public enum PlayerCondition
{
    None = 0,
    Downed = 1 << 0,        // 넉다운/다이빙으로 쓰러져 있음
    Grabbed = 1 << 1,       // 남에게 붙잡힘
    Grabbing = 1 << 2,      // 남을 붙잡는 중(손 점유)
    HoldingItem = 1 << 3,   // 1~3 슬롯 아이템을 손에 듦(손 점유)
    PaintGunOut = 1 << 4,   // 4 슬롯 페인트 총을 손에 듦(손 점유)
    Reaching = 1 << 5,      // 잡기 키를 누르는 중(아직 못 잡았어도 손을 뻗고 있음)
    DraggingObject = 1 << 6, // 드래그 오브젝트를 끌고 있음(손 점유)
}

/// <summary>
/// 플레이어 행동의 상호 배타 규칙을 한곳에서 판정한다. <br/>
/// 상태 소스가 전부 NetworkVariable이라 Owner 선검사와 서버 재검증에 같은 함수를 쓴다.
/// </summary>
public class PlayerActionGate : MonoBehaviour
{
    /// <summary>페인트 총이 들어가는 인벤토리 슬롯</summary>
    public const int PAINT_GUN_SLOT = 4;

    // 행동 하나의 규칙. blocked가 하나라도 켜져 있으면 불가, required는 전부 켜져 있어야 가능.
    private readonly struct Rule
    {
        public readonly Cond Blocked;
        public readonly Cond Required;

        public Rule(Cond blocked, Cond required = Cond.None)
        {
            Blocked = blocked;
            Required = required;
        }
    }

    private const Cond BUSY = Cond.Downed | Cond.Grabbing | Cond.DraggingObject;

    private const Cond RESTRAINED = BUSY | Cond.Grabbed;

    // 상호 배타 규칙의 유일한 정의. 새 행동이나 제약은 여기만 고치면 된다.
    // 표에 없는 행동은 항상 허용되므로 새 행동은 반드시 한 줄 추가할 것.
    private static readonly Dictionary<PlayerAction, Rule> RULES = new()
    {
        // 쓰러졌거나 붙잡히면 발이 묶인다
        [PlayerAction.Jump] = new Rule(Cond.Downed | Cond.Grabbed | Cond.DraggingObject),

        // 손이 비어야 친다(잡기 키를 누르는 동안도 손 점유). 붙잡힌 상태에선 반격/탈출용으로 허용
        [PlayerAction.Punch] = new Rule(BUSY | Cond.Reaching | Cond.HoldingItem | Cond.PaintGunOut),

        // 손이 비어야 잡는다. 이미 잡고 있거나 붙잡혔으면 불가
        [PlayerAction.Grab] = new Rule(RESTRAINED | Cond.HoldingItem | Cond.PaintGunOut),

        // 잡고 있어도 가능(서버가 먼저 놓는다). 쓰러진 동안엔 기상 후에
        [PlayerAction.Dive] = new Rule(Cond.Downed | Cond.Grabbed | Cond.DraggingObject),

        // 붙잡혀도 손은 쓸 수 있으므로 아이템/도구 조작은 전부 허용.
        [PlayerAction.Pickup] = new Rule(BUSY | Cond.HoldingItem),
        [PlayerAction.DropItem] = new Rule(BUSY | Cond.PaintGunOut, Cond.HoldingItem),
        [PlayerAction.ChangeSlot] = new Rule(BUSY),
        [PlayerAction.ToggleGun] = new Rule(BUSY),
        [PlayerAction.PlaceBox] = new Rule(BUSY, Cond.HoldingItem),
        [PlayerAction.UseTool] = new Rule(BUSY, Cond.HoldingItem),
        [PlayerAction.FirePaint] = new Rule(BUSY, Cond.PaintGunOut),

        [PlayerAction.ToggleLight] = new Rule(RESTRAINED),

        [PlayerAction.SpawnTrash] = new Rule(RESTRAINED | Cond.HoldingItem),
        // 끌기 시작 조건 (이미 끌고 있거나 손에 무언가 들려있을 때는 끌기 시작 불가)
        [PlayerAction.DragObject] = new Rule(RESTRAINED | Cond.HoldingItem | Cond.PaintGunOut),
    };

    private PlayerKnockdown _knockdown;
    private PlayerGrab _grab;
    private PlayerInventory _inventory;
    private PlayerMovement _movement;
    private void Awake()
    {
        _knockdown = GetComponent<PlayerKnockdown>();
        _grab = GetComponent<PlayerGrab>();
        _inventory = GetComponent<PlayerInventory>();
        _movement = GetComponent<PlayerMovement>();
    }
    /// <summary>플레이어에게서 게이트를 얻는다. 프리팹에 없으면 붙여서 사용</summary>
    /// <param name="player">Player 루트 오브젝트</param>
    public static PlayerActionGate GetOrAdd(GameObject player)
    {
        return player.TryGetComponent(out PlayerActionGate gate)
            ? gate
            : player.AddComponent<PlayerActionGate>();
    }

    /// <summary>슬롯 전환에 해당하는 행동</summary>
    /// <param name="targetSlot">바꾸려는 슬롯 번호</param>
    public static PlayerAction SlotChangeAction(int targetSlot)
    {
        return targetSlot == PAINT_GUN_SLOT ? PlayerAction.ToggleGun : PlayerAction.ChangeSlot;
    }

    /// <summary>현재 켜져 있는 상태 플래그</summary>
    public Cond CurrentConditions
    {
        get
        {
            Cond conditions = Cond.None;

            if (_knockdown != null && _knockdown.IsDown)
            {
                conditions |= Cond.Downed;
            }

            if (_grab != null)
            {
                if (_grab.IsGrabbed)
                {
                    conditions |= Cond.Grabbed;
                }

                if (_grab.IsGrabbing)
                {
                    conditions |= Cond.Grabbing;
                }

                if (_grab.IsReaching)
                {
                    conditions |= Cond.Reaching;
                }
            }

            if (_inventory != null)
            {
                if (_inventory.GetCurrentEquippedItem() != null)
                {
                    conditions |= Cond.HoldingItem;
                }

                if (_inventory.CurrentSlot == PAINT_GUN_SLOT)
                {
                    conditions |= Cond.PaintGunOut;
                }
                // 플레이어가 현재 기구를 끌고 있는지 확인
                if (_movement != null && _movement.IsDraggingObject)
                {
                    conditions |= Cond.DraggingObject;
                }
            }

            return conditions;
        }
    }

    /// <summary>지금 이 행동을 할 수 있는지. Owner 선검사와 서버 재검증 양쪽에서 호출한다.</summary>
    /// <param name="action">판정할 행동</param>
    public bool CanDo(PlayerAction action)
    {
        if (!RULES.TryGetValue(action, out Rule rule))
        {
            return true;
        }

        Cond conditions = CurrentConditions;
        return (conditions & rule.Blocked) == Cond.None
            && (conditions & rule.Required) == rule.Required;
    }
}
