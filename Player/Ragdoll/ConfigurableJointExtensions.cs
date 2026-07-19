using UnityEngine;

/// <summary>
/// 로컬 회전을 ConfigurableJoint 좌표계의 목표 회전으로 변환
/// </summary>
public static class ConfigurableJointExtensions
{
    /// <summary>
    /// 관절의 로컬 목표 회전 설정
    /// </summary>
    /// <param name="joint">목표 회전을 설정할 관절</param>
    /// <param name="targetLocalRotation">목표 로컬 회전</param>
    /// <param name="startLocalRotation">관절 생성 시점의 로컬 회전</param>
    public static void SetTargetRotationLocal(
        this ConfigurableJoint joint,
        Quaternion targetLocalRotation,
        Quaternion startLocalRotation)
    {
        if (joint.configuredInWorldSpace)
        {
            Debug.LogError("configuredInWorldSpace가 켜진 관절엔 쓸 수 없다.", joint);
            return;
        }

        // 관절 축을 기준으로 로컬 좌표계 구성
        Vector3 right = joint.axis;
        Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
        Vector3 up = Vector3.Cross(forward, right).normalized;
        Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);

        Quaternion result = Quaternion.Inverse(worldToJointSpace);
        result *= Quaternion.Inverse(targetLocalRotation) * startLocalRotation;
        result *= worldToJointSpace;

        joint.targetRotation = result;
    }
}
