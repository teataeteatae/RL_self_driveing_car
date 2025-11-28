using System.Collections.Generic;
using UnityEngine;

public class ObstacleSpawner : MonoBehaviour
{
    [Header("References")]
    public GameObject obstaclePrefab; // 장애물 프리팹
    public Transform startPoint;      // 스폰 시작 지점 (SpawnStart)
    public Transform endPoint;        // 스폰 끝 지점   (SpawnEnd)

    [Header("Spawn Settings")]
    public int obstacleCount = 5;         // 한 에피소드당 장애물 개수
    public float laneHalfWidth = 2f;      // 도로 중심에서 좌우로 얼마나 퍼트릴지
    public float minDistanceFromCar = 5f; // 차랑 너무 가깝게는 안 만들기

    private List<GameObject> spawned = new List<GameObject>();

    /// <summary>
    /// 에피소드 시작할 때 CarAgent에서 호출해줄 함수.
    /// </summary>
    public void ResetObstacles(Transform carTransform)
    {
        // 1) 기존 장애물 삭제
        foreach (var obj in spawned)
        {
            if (obj != null)
                Destroy(obj);
        }
        spawned.Clear();

        // 레퍼런스가 비어 있으면 아무 것도 안 함
        if (obstaclePrefab == null || startPoint == null || endPoint == null)
            return;

        // 2) 새 장애물 생성
        for (int i = 0; i < obstacleCount; i++)
        {
            Vector3 pos = GetRandomPosition(carTransform);
            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject o = Instantiate(
                obstaclePrefab,
                pos,
                rot,
                transform            // 이 스포너(보통 Cararea/Road) 밑으로 붙이기
            );

            spawned.Add(o);
        }
    }

    // 차와 너무 가까운 위치는 피하면서 랜덤 위치 반환
    private Vector3 GetRandomPosition(Transform carTransform)
    {
        Vector3 pos;
        int safety = 0;

        do
        {
            // startPoint ~ endPoint 사이 아무 위치 (t: 0~1)
            float t = Random.value;
            Vector3 forwardPos = Vector3.Lerp(startPoint.position, endPoint.position, t);

            // 도로 중심에서 좌우 랜덤
            float side = Random.Range(-laneHalfWidth, laneHalfWidth);
            // 이 스포너의 오른쪽 방향(transform.right)을 기준으로 좌우 퍼뜨리기
            pos = forwardPos + transform.right * side;

            pos.y = forwardPos.y; // 높이 맞추기

            safety++;
            if (safety > 20) break; // 혹시라도 무한루프 예방
        }
        while (Vector3.Distance(pos, carTransform.position) < minDistanceFromCar);

        return pos;
    }
}
