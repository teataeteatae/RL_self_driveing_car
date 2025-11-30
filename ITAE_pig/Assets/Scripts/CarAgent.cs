using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class CarAgent : Agent
{
    [Header("References")]
    public PrometeoCarController car;
    public Transform spawnPoint;
    public Transform trackCenter;

    private Rigidbody rb;

    [Header("Rewards / Penalties")]
    // 역주행 패널티 강화 (기존 -0.002f -> -0.01f)
    public float backwardPenaltyScale = -0.01f; 

    // 가만히 있는 것 감지
    private int idleSteps = 0;
    // 수정: 400 -> 150 (더 빨리 포기하고 다시 시작하도록)
    public int maxIdleSteps = 150;   

    // 🔥 충돌 횟수 관리
    private int collisionCount = 0;
    // 수정: 10 -> 3 (벽에 3번 쿵쿵거리면 바로 종료)
    public int maxCollisionCount = 3;   

    public override void Initialize()
    {
        if (car == null)
            car = GetComponent<PrometeoCarController>();

        if (car != null)
            rb = car.GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        if (car == null || rb == null) return;

        // --- 위치/회전 초기화 ---
        if (spawnPoint != null)
        {
            car.transform.position = spawnPoint.position;
            car.transform.rotation = spawnPoint.rotation;
        }
        else
        {
            car.transform.localPosition = Vector3.zero;
            car.transform.localRotation = Quaternion.identity;
        }

        // --- 물리 초기화 ---
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // --- Prometeo 차량 상태 초기화 ---
        car.ThrottleOff();
        car.ResetSteeringAngle();
        car.RecoverTraction();

        // 카운터 리셋
        idleSteps = 0;
        collisionCount = 0;
    }
    private void EndEpisodeWithLog(string reason)
    {
        Debug.Log($"[CarAgent] EndEpisode: {reason}");
        EndEpisode();
    }
    public override void CollectObservations(VectorSensor sensor)
    {
        if (car == null || rb == null)
        {
            
        }

        // 1) 속도 (정규화)
        sensor.AddObservation(rb.velocity.magnitude / 20f);

        // 2) 트랙 중심과의 거리
        if (trackCenter != null)
        {
            float dist = Vector3.Distance(car.transform.position, trackCenter.position);
            sensor.AddObservation(dist / 10f); // 값 범위에 따라 나누는 값 조정 필요
        }
        else
        {
            sensor.AddObservation(0f);
        }

        // 3) 차의 앞방향 벡터 (Vector3)
        sensor.AddObservation(car.transform.forward);

        // 4) Y축 위치 (낙하 감지용)
        sensor.AddObservation(car.transform.position.y);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (car == null || rb == null) return;

        // 입력값 클램핑
        float steer = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float throttle = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

        // =============================
        // 🔸 액션 → 차 조작
        // =============================
        
        // Steering
        if (steer > 0.1f) car.TurnRight();
        else if (steer < -0.1f) car.TurnLeft();
        else car.ResetSteeringAngle();

        // Throttle (전진/후진)
        if (throttle > 0.1f) car.GoForward();
        else if (throttle < -0.1f) car.GoReverse();
        else car.ThrottleOff();

        // =============================
        // 🔹 보상 설계 (개선됨)
        // =============================

        float forwardSpeed = Vector3.Dot(rb.velocity, car.transform.forward);
        float speedMag = rb.velocity.magnitude;

        // 1. 시간 패널티 (기존보다 약간 강화: 빨리 가라고 독촉)
        // 기존 -0.0005f -> -0.001f
        AddReward(-0.001f);

        // 2. 전진 보상 (속도가 빠를수록 큰 보상)
        if (forwardSpeed > 0.1f)
        {
            float normForward = Mathf.Clamp01(forwardSpeed / 20f); // 분모를 최고속도에 맞게 조정
            AddReward(0.1f * normForward);
        }

        // 3. 멈춤(Idle) 패널티 및 종료 처리
        // 속도가 아주 느리거나(0.5f 이하), 사실상 멈춰있을 때
        if (speedMag < 0.2f) 
        {
            // 가만히 있으면 벌점을 세게 줘서 움직이게 유도
            AddReward(-0.002f); 
            idleSteps++;
        }
        else
        {
            // 움직이면 초기화
            idleSteps = 0;
        }

        // 너무 오래 멈춰있으면 강제 종료 (학습 속도 향상 핵심)
        if (idleSteps > maxIdleSteps)
        {
            AddReward(-1.0f); // 큰 벌점 주고 종료
            EndEpisodeWithLog("IdleStepsExceeded");
            return;
        }

        // 4. 역주행 패널티
        if (forwardSpeed < -0.1f)
        {
            // 속도 비례해서 벌점
            float normBackward = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 10f);
            AddReward(backwardPenaltyScale * normBackward);
        }

        // 5. 낙하 감지 (트랙 밖으로 떨어졌을 때)
        if (car.transform.position.y < -5f)
        {
            AddReward(-1.0f);
            EndEpisodeWithLog("FellOffTrack");
        }
    }

    // 🔻 충돌 처리 (이제 횟수 초과시 종료됨)
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("SideLine"))
        {
            collisionCount++;
            AddReward(-0.1f); // 충돌 즉시 감점

            // 설정한 횟수보다 많이 부딪히면 "운전 불가"로 판단하고 종료
            if (collisionCount > maxCollisionCount)
            {
                AddReward(-0.5f); // 추가 벌점
                EndEpisodeWithLog("TooManyCollisions");
            }
        }
    }

    // 🔻 트리거 보상
    private void OnTriggerEnter(Collider other)
    {
        // 중앙선 유지 보상
        if (other.CompareTag("CarCenter"))
        {
            AddReward(0.01f); // 너무 크면 중앙선만 밟고 있으려 하므로 작게
        }
        
        // 체크포인트 통과 (가장 큰 보상이 되어야 함)
        if (other.CompareTag("checkpoint"))
        {
            AddReward(1.0f); // 확실한 동기 부여
            
            // 체크포인트 통과시 Idle 타이머 초기화 (잘 가고 있으니까)
            idleSteps = 0; 
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        ca[0] = Input.GetAxis("Horizontal"); 
        ca[1] = Input.GetAxis("Vertical");   
    }
}