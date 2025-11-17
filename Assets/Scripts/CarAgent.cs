using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class CarAgent : Agent
{
    [Header("References")]
    public PrometeoCarController car;   // PrometeoCarController 달린 차
    public Transform spawnPoint;        // 시작 위치
    public Transform trackCenter;       // 도로 중심 기준(옵션)

    Rigidbody rb;

    // 차가 조금만 떨어져도 감지하도록 설정
    private float fallThresholdY = -1.0f;   // y가 이 값 아래면 즉시 리셋

    public override void Initialize()
    {
        if (car == null)
        {
            car = GetComponent<PrometeoCarController>();
        }

        if (car != null)
        {
            rb = car.GetComponent<Rigidbody>();
        }
    }

    public override void OnEpisodeBegin()
    {
        if (car == null || rb == null) return;

        // 위치/회전 리셋
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

        // 속도 리셋
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Prometeo 기본값 리셋
        car.ThrottleOff();
        car.ResetSteeringAngle();
        car.RecoverTraction();
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

        // 1) 속도
        float speed = rb.velocity.magnitude;
        sensor.AddObservation(speed / 20f);

        // 2) 도로 중심 거리
        if (trackCenter != null)
        {
            float dist = Vector3.Distance(car.transform.position, trackCenter.position);
            sensor.AddObservation(dist / 10f);
        }
        else
        {
            sensor.AddObservation(0f);
        }

        // 3) 차의 전방 방향
        sensor.AddObservation(car.transform.forward);

        // 4) 떨어지는지 확인 (y값)
        sensor.AddObservation(car.transform.position.y);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (car == null || rb == null) return;

        float steer = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float throttle = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

        // --- 조향 ---
        if (steer > 0.1f)
            car.TurnRight();
        else if (steer < -0.1f)
            car.TurnLeft();
        else
            car.ResetSteeringAngle();

        // --- 가속/후진 ---
        if (throttle > 0.1f)
            car.GoForward();
        else if (throttle < -0.1f)
            car.GoReverse();
        else
            car.ThrottleOff();

        // --- 보상 ---
        float forwardSpeed = Vector3.Dot(rb.velocity, car.transform.forward);
        AddReward(0.0005f * Mathf.Max(0f, forwardSpeed));

        if (trackCenter != null)
        {
            float dist = Vector3.Distance(car.transform.position, trackCenter.position);
            float centerReward = Mathf.Clamp01(1f - (dist / 5f));
            AddReward(0.0005f * centerReward);
        }

        if (rb.velocity.magnitude < 0.5f)
            AddReward(-0.0005f);

        // ============================
        // 🚨 떨어짐 감지 (중요)
        // ============================
        if (car.transform.position.y < fallThresholdY)
        {
            AddReward(-1f);
            EndEpisode();
            return;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        ca[0] = Input.GetAxis("Horizontal");
        ca[1] = Input.GetAxis("Vertical");
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("Obstacle") ||
            collision.collider.CompareTag("Wall"))
        {
            AddReward(-1f);
            EndEpisode();
        }
    }
}
