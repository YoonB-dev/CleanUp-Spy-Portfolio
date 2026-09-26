using UnityEngine;

/// <summary>
/// 씬이 바뀌어도 살아남는 MonoBehaviour 싱글톤. 씬에 없으면 처음 접근할 때 빈 GameObject로 자동 생성한다.
/// (씬 하나에서만 쓰는 싱글톤은 SceneSingleton을 사용)
/// 상속한 쪽에서 Awake를 재정의하면 base.Awake() 호출 후 Existing != this면 바로 return 해야 한다 (중복 생성분 파괴 대기 중).
/// </summary>
public class PersistentSingleton<T> : MonoBehaviour where T : PersistentSingleton<T>
{
    private static T instance;

    /// 없으면 만들어 낸다. 게임 종료 중에는 새로 만들지 않고 null
    public static T Instance
    {
        get
        {
            if (instance != null || SingletonShutdown.IsQuitting) return instance;

            new GameObject(typeof(T).Name).AddComponent<T>();   // Awake에서 instance가 채워진다
            return instance;
        }
    }

    /// <summary>이미 만들어진 것만 본다. 종료 중에 다시 만들지 않으려는 쪽(OnDestroy 등)에서 쓴다</summary>
    public static T Existing => instance;

    protected virtual void Awake()
    {
        if (instance == null)
        {
            instance = (T)this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    protected virtual void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}

/// <summary>
/// 게임 종료 여부 공유용. 제네릭 클래스에는 RuntimeInitializeOnLoadMethod를 걸 수 없어 따로 둔다.
/// 도메인 리로드를 꺼둔 에디터에서도 플레이할 때마다 초기화된다.
/// </summary>
public static class SingletonShutdown
{
    public static bool IsQuitting { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        IsQuitting = false;
        Application.quitting -= OnQuitting;
        Application.quitting += OnQuitting;
    }

    private static void OnQuitting() => IsQuitting = true;
}
