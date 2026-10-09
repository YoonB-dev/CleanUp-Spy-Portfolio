using UnityEngine;

[CreateAssetMenu(fileName = "TrashData", menuName = "CleanUpMafia/Trash Data")]
public class TrashData : ItemData // 표시 이름(displayName, 기존 trashName)과 아이콘은 ItemData에서 상속
{
    [Header("1. Visuals")]
    public Mesh trashMesh;
    public Material trashMaterial;
    public Vector3 modelScale = Vector3.one; // 모델마다 기본 크기가 다를 때 조정용 근데 지금 캐릭터 pickup을 할때 1을 기준으로 동작해서 이거 나중에 바꾸려면 관련 부분 수정해야함. 일단 귀찮아서 냅둠

    [Header("2. Gameplay Stats")]
    public int score = 10;          // 쓰레기가 차지하는 점수

    [Header("3. FX & Audio")]
    public GameObject cleanVFX;    // 청소 완료 파티클 프리팹
    public AudioClip cleanSFX;     // 청소 완료 사운드
    public AudioClip pickupSFX;   // 쓰레기 줍는 사운드
}