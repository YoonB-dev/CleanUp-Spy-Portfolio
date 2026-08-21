using UnityEngine;

/// <summary>
/// 양손으로 드는 아이템에 붙여, 좌우 손이 향할 그립 지점을 지정한다.
/// </summary>
public class CarryGripPoints : MonoBehaviour
{
    [SerializeField] private Transform _leftGripPoint;
    [SerializeField] private Transform _rightGripPoint;

    public Transform LeftGripPoint => _leftGripPoint;
    public Transform RightGripPoint => _rightGripPoint;
}