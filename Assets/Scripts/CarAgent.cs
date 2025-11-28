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

    public ObstacleSpawner obstacleSpawner;   // 🔥 장애물 스포너 (Inspector에서 Cararea 드래그)

    private Rigidbody rb;

    [Header("Rewards / Penalties")]
    public float backwardPenaltyScale = -0.002f;

    private int idleSteps = 0;
    public int maxIdleSteps = 400;

    private int collisionCount = 0;
    public int maxCollisionCount = 10;

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

        // --- 위치/회전 리셋 ---
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

        // --- 물리 리셋 ---
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // --- 차량 제어 상태 리셋 ---
        car.ThrottleOff();
        car.ResetSteeringAngle();
        car.RecoverTraction();

        idleSteps = 0;
        collisionCount = 0;

        // 🔥 장애물 리셋
        if (obstacleSpawner != null)
        {
            obstacleSpawner.ResetObstacles(car.transform);
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (car == null || rb == null)
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
            return;
        }

        // 1) 속도
        sensor.AddObservation(rb.velocity.magnitude / 20f);

        // 2) 트랙 중앙과의 거리
        if (trackCenter != null)
        {
            float dist = Vector3.Distance(car.transform.position, trackCenter.position);
            sensor.AddObservation(dist / 10f);
        }
        else
        {
            sensor.AddObservation(0f);
        }

        // 3) 전방 방향
        sensor.AddObservation(car.transform.forward);

        // 4) Y 위치
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

        // --- 가속 / 후진 ---
        if (throttle > 0.1f)
            car.GoForward();
        else if (throttle < -0.1f)
            car.GoReverse();
        else
            car.ThrottleOff();

        float forwardSpeed = Vector3.Dot(rb.velocity, car.transform.forward);
        float speedMag = rb.velocity.magnitude;

        // 0) 시간 패널티
        AddReward(-0.0005f);

        // 1) 전진 보상
        float normForward = Mathf.Clamp01(forwardSpeed / 10f);
        AddReward(0.5f * normForward);

        // 2) 중앙 유지 보상
        if (trackCenter != null)
        {
            float dist = Vector3.Distance(car.transform.position, trackCenter.position);
            if (dist < 2.0f)
            {
                float centerReward = 1f - (dist / 2f);
                AddReward(0.002f * centerReward);
            }
        }

        // 3) 너무 느릴 때 패널티 + idle 증가
        if (speedMag < 0.2f)
        {
            AddReward(-0.002f);
            idleSteps++;
        }
        else
        {
            idleSteps = 0;
        }

        // idle 한계 넘으면 에피소드 종료
        if (idleSteps > maxIdleSteps)
        {
            AddReward(-0.5f);
            EndEpisode();
            return;
        }

        // 역주행 패널티
        if (forwardSpeed < -0.5f)
        {
            float normBackward = Mathf.Clamp01(-forwardSpeed / 10f);
            AddReward(backwardPenaltyScale * normBackward);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("SideLine"))
        {
            AddReward(-0.2f);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("CarCenter"))
        {
            AddReward(0.05f);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        ca[0] = Input.GetAxis("Horizontal"); // 좌우
        ca[1] = Input.GetAxis("Vertical");   // 전후
    }
}
