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
            sensor.AddObservation(0f);            // speed
            sensor.AddObservation(0f);            // dist
            sensor.AddObservation(Vector3.zero);  // forward
            sensor.AddObservation(0f);            // y
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

        float forwardSpeed = Vector3.Dot(rb.velocity, car.transform.forward);
        float speedMag = rb.velocity.magnitude;

        // 0) 매 스텝 시간 패널티 (괜히 오래 버티는 전략 방지)
        AddReward(-0.0005f);

        // 1) 앞으로 가면 +보상 (정면 진행 성분 기준)
        float normForward = Mathf.Clamp01(forwardSpeed / 10f);
        AddReward(0.2f * normForward);

        // 2) 도로 중심에 가까울수록 + 보상
        if (trackCenter != null)
        {
            float dist = Vector3.Distance(car.transform.position, trackCenter.position);
            if (dist < 2.0f)
            {
                float centerReward = 1f - (dist / 2f); // 0 ~ 1
                AddReward(0.002f * centerReward);
            }
        }

        // 3) 너무 느리면 패널티 + idle 처리
        if (speedMag < 0.5f)
        {
            AddReward(-0.002f); // 가만히 있으면 손해
            idleSteps++;
        }
        else
        {
            idleSteps = 0;
        }

        // 너무 오래 안 움직이면 강제 종료
        if (idleSteps > maxIdleSteps)
        {
            AddReward(-0.5f);
            EndEpisode();
            return;
        }

        // 4) 역주행(뒤로 가기) 패널티
        if (forwardSpeed < -0.1f)
        {
            float normBackward = Mathf.Clamp01(-forwardSpeed / 10f);
            AddReward(backwardPenaltyScale * normBackward); // backwardPenaltyScale는 음수
        }
    }

    // 🔻 충돌 처리 (한 번만 정의!)
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("RightLine"))
        {
            AddReward(-0.5f);
            // 필요하면 EndEpisode(); 추가
            return;
        }

        if (collision.collider.CompareTag("CenterLine"))
        {
            AddReward(-0.3f);
            // 필요하면 EndEpisode(); 추가
            return;
        }
    }

    // 🔻 중앙 콜라이더 트리거 리워드
    // Collider_Center 태그 가진 트리거 안에 있을 때 매 스텝마다 +리워드
    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Collider_Center"))
        {
            // 중앙에 잘 붙어서 달릴수록 이득
            AddReward(0.005f);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        ca[0] = Input.GetAxis("Horizontal");
        ca[1] = Input.GetAxis("Vertical");
    }
}
