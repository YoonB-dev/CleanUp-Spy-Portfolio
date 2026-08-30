using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 여기서 받은 클릭이 부모로 올라가지 않게 막는다.
/// 딤 배경 위에 얹은 패널처럼 바깥을 눌렀을 때만 닫혀야 하는 곳에 붙인다.
/// </summary>
public class ClickCatcher : MonoBehaviour, IPointerClickHandler
{
    // 아무것도 하지 않아도 여기서 처리한 것으로 쳐서 전파가 멈춘다
    public void OnPointerClick(PointerEventData eventData) { }
}
