using System.Collections.Generic;
using UnityEngine;

public class ObstacleManager : MonoBehaviour
{
    [Header("Spawn Settings")]
    public GameObject obstaclePrefab;    // Obstacle 프리팹
    public Transform[] spawnPoints;      // ObstacleSpawn_1 ~ 8
    public int minObstacles = 1;
    public int maxObstacles = 3;

    private readonly List<GameObject> spawned = new List<GameObject>();

    public void ResetObstacles()
    {
        // 1) 이전 에피소드 장애물 삭제
        foreach (var o in spawned)
        {
            if (o != null) Destroy(o);
        }
        spawned.Clear();

        if (obstaclePrefab == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("ObstacleManager: prefab 또는 spawnPoints가 비어있음");
            return;
        }

        // 2) 이번 에피소드에 몇 개 뽑을지
        int count = Random.Range(minObstacles, maxObstacles + 1);

        var used = new HashSet<int>();

        for (int i = 0; i < count; i++)
        {
            int idx;
            // 같은 스폰 포인트 두 번 안 쓰게
            do
            {
                idx = Random.Range(0, spawnPoints.Length);
            } while (used.Contains(idx));

            used.Add(idx);

            Transform sp = spawnPoints[idx];
            GameObject obs = Instantiate(obstaclePrefab, sp.position, sp.rotation, transform);
            spawned.Add(obs);
        }
    }

    // 디버그용: 그냥 Play만 눌러도 장애물 뜨는지 보고 싶으면 Start에서 한번 호출
    // void Start() { ResetObstacles(); }
}
