using UnityEngine;

/// <summary>
/// 회전 동기화용 smallest-three 쿼터니언 압축 (32비트)
/// </summary>
public static class QuaternionCompression
{
    private const float RANGE_MIN = -0.7071068f;
    private const float RANGE_MAX = 0.7071068f;
    private const int COMPONENT_BITS = 10;
    private const uint COMPONENT_MASK = (1u << COMPONENT_BITS) - 1;

    /// <summary>
    /// 쿼터니언을 32비트로 압축. 상위 2비트=최대 성분 인덱스, 나머지 3*10비트=성분
    /// </summary>
    public static uint Compress(Quaternion rotation)
    {
        rotation = Normalize(rotation);

        int maxIndex = 0;
        float maxAbs = -1f;
        for (int i = 0; i < 4; i++)
        {
            float abs = Mathf.Abs(Get(rotation, i));
            if (abs > maxAbs)
            {
                maxAbs = abs;
                maxIndex = i;
            }
        }

        // 최대 성분을 양수로 고정 (복원 시 부호 정보 불필요)
        float sign = Get(rotation, maxIndex) < 0f ? -1f : 1f;

        uint result = (uint)maxIndex << 30;
        int shift = 20;
        for (int i = 0; i < 4; i++)
        {
            if (i == maxIndex)
            {
                continue;
            }

            float normalized = (Get(rotation, i) * sign - RANGE_MIN) / (RANGE_MAX - RANGE_MIN);
            uint quantized = (uint)Mathf.RoundToInt(Mathf.Clamp01(normalized) * COMPONENT_MASK);
            result |= (quantized & COMPONENT_MASK) << shift;
            shift -= COMPONENT_BITS;
        }

        return result;
    }

    /// <summary>
    /// 압축된 32비트를 쿼터니언으로 복원
    /// </summary>
    public static Quaternion Decompress(uint data)
    {
        int maxIndex = (int)(data >> 30);

        float x = 0f, y = 0f, z = 0f, w = 0f;
        float sumSquares = 0f;
        int shift = 20;
        for (int i = 0; i < 4; i++)
        {
            if (i == maxIndex)
            {
                continue;
            }

            uint quantized = (data >> shift) & COMPONENT_MASK;
            float value = (float)quantized / COMPONENT_MASK * (RANGE_MAX - RANGE_MIN) + RANGE_MIN;
            Set(ref x, ref y, ref z, ref w, i, value);
            sumSquares += value * value;
            shift -= COMPONENT_BITS;
        }

        float maxValue = Mathf.Sqrt(Mathf.Max(0f, 1f - sumSquares));
        Set(ref x, ref y, ref z, ref w, maxIndex, maxValue);
        return new Quaternion(x, y, z, w);
    }

    private static Quaternion Normalize(Quaternion q)
    {
        float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (mag < 1e-6f)
        {
            return Quaternion.identity;
        }

        return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
    }

    private static float Get(Quaternion q, int index)
    {
        switch (index)
        {
            case 0: return q.x;
            case 1: return q.y;
            case 2: return q.z;
            default: return q.w;
        }
    }

    private static void Set(ref float x, ref float y, ref float z, ref float w, int index, float value)
    {
        switch (index)
        {
            case 0: x = value; break;
            case 1: y = value; break;
            case 2: z = value; break;
            default: w = value; break;
        }
    }
}
