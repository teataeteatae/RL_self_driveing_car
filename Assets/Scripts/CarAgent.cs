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

    Rigidbody rb;

    // 연석/중앙선/역주행 패널티 세기
    [Header("Rewards / Penalties")]
    public float backwardPenaltyScale = -0.002f; // 역주행 속도 * 이 값

    // 너무 오래 안 움직이는 경우 처리
    private int idleSteps = 0;
    public int maxIdleSteps = 80;   // 이만큼 연속으로 느리면 에피소드 종료

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

        // --- Reset position & rotation ---
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

        // --- Reset rigidbody ---
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // --- Reset Prometeo ---
        car.ThrottleOff();
        car.ResetSteeringAngle();
        car.RecoverTraction();

        // 멈춤 스텝 카운터 리셋
        idleSteps = 0;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (car == null || rb == null)
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(Vector3.zero);
            return;
        }

        // 1) speed normalized
        sensor.AddObservation(rb.velocity.magnitude / 20f);

        // 2) distance from track center
        if (trackCenter != null)
        {
            float dist = Vector3.Distance(car.transform.position, trackCenter.position);
            sensor.AddObservation(dist / 10f);
        }
        else
        {
            sensor.AddObservation(0f);
        }

        // 3) forward direction (3 floats)
        sensor.AddObservation(car.transform.forward);

        // 4) y position (for 안정성)
        sensor.AddObservation(car.transform.position.y);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (car == null || rb == null) return;

        float steer = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float throttle = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

        // =============================
        // 🔸 액션 → 차 조작
        // =============================

        // Steering
        if (steer > 0.1f)
            car.TurnRight();
        else if (steer < -0.1f)
            car.TurnLeft();
        else
            car.ResetSteeringAngle();

        // Throttle / Reverse
        if (throttle > 0.1f)
            car.GoForward();
        else if (throttle < -0.1f)
            car.GoReverse();
        else
            car.ThrottleOff();
// =============================
// 🔹 보상 설계
// =============================

float forwardSpeed = Vector3.Dot(rb.velocity, car.transform.forward); // 전방 속도
float speedMag = rb.velocity.magnitude;

// 0) 매 스텝 시간 패널티 (괜히 오래 버티는 전략 방지)
AddReward(-0.0005f);

// 1) 앞으로 가면 +보상
//    forwardSpeed를 0~1 사이로 대략 정규화해서 사용
float normForward = Mathf.Clamp01(forwardSpeed / 10f);
AddReward(0.2f * normForward);   // 앞으로 가는 게 핵심 리워드

// 2) 도로 중심에 가까울수록 +보상
if (trackCenter != null)
{
    float dist = Vector3.Distance(car.transform.position, trackCenter.position);
    if (dist < 2.0f) // 일정 범위 안일 때만
    {
        float centerReward = 1f - (dist / 2f);
        AddReward(0.002f * centerReward);
    }
}

// 3) 너무 느리면 패널티 + idle 카운트 증가
if (speedMag < 0.5f)
{
    // 가만히 있으면 더 손해
    AddReward(-0.002f);
    idleSteps++;
}
else
{
    idleSteps = 0;
}

// 너무 오래 안 움직이면 강제 종료
if (idleSteps > maxIdleSteps)
{
    AddReward(-0.5f);  // "시간만 끈 실패"
    EndEpisode();
    return;
}

// 4) 🚫 역주행(뒤로 가기) 소프트 패널티
if (forwardSpeed < -0.1f)
{
    // 뒤로 갈수록 약간씩 손해 (하지만 즉사 아님)
    float normBackward = Mathf.Clamp01(-forwardSpeed / 10f);
    AddReward(-0.01f * normBackward);
}
}

// ==================================================================
// 🚧 충돌 처리 (소프트 정책)
// ==================================================================
private void OnCollisionEnter(Collision collision)
{
    // 🔸 오른쪽 연석/보도: 꽤 큰 패널티, 하지만 에피소드 유지
    if (collision.collider.CompareTag("RightLine"))
    {
        AddReward(-0.5f);   // 한 번만 밟아도 많이 아픔
        return;
    }

    // 🔸 중앙선: 연석보다 조금 약한 패널티
    if (collision.collider.CompareTag("CenterLine"))
    {
        AddReward(-0.3f);   // 중앙선 살짝 밟는 것도 손해
        return;
    }

    
}


    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        ca[0] = Input.GetAxis("Horizontal");
        ca[1] = Input.GetAxis("Vertical");
    }
}
