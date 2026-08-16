using UnityEngine;
using UnityEngine.InputSystem;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

[RequireComponent(typeof(Rigidbody))]
public class PlayerAgent : Agent
{
    [Tooltip("이동 힘 배율 (레퍼런스 moveSpeed)")]
    public float moveSpeed = 2f;
    [Tooltip("회전 속도 (deg/s, 레퍼런스 turnSpeed)")]
    public float turnSpeed = 300f;
    [Tooltip("애니메이션 재생 속도 배율 (1 = 원본)")]
    public float animationSpeed = 1f;

    Rigidbody rb;
    Animator animator;

    float m_Forward;
    float m_Rotate;

    [SerializeField] FoodArea m_FoodArea;
    private float spawnRange = 10f;
    public float meatReward = 1f;
    public float carrotReward = -1f;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        
        m_FoodArea = GetComponentInParent<FoodArea>();
    }

    public override void OnEpisodeBegin()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.localPosition = new Vector3(Random.Range(-spawnRange, spawnRange), 0.6f, Random.Range(-spawnRange, spawnRange));
        transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
        m_FoodArea.ResetFoods();
        //base.OnEpisodeBegin();
        
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        m_Forward = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        m_Rotate  = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        
        AddReward(-1f/MaxStep);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        var localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVelocity.x);
        sensor.AddObservation(localVelocity.z);
        
        //base.CollectObservations(sensor);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    ca[0] = 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  ca[0] = -1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) ca[1] = 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  ca[1] = -1f;
    }

    void Update()
    {
        SetAnimation();
    }

    void FixedUpdate()
    {
        MoveAgent();
    }

    void MoveAgent()
    {
        Vector3 dirToGo = transform.forward * m_Forward;
        Vector3 rotateDir = transform.up * m_Rotate;

        rb.AddForce(dirToGo * moveSpeed, ForceMode.VelocityChange);
        transform.Rotate(rotateDir, Time.fixedDeltaTime * turnSpeed);

        if (rb.linearVelocity.sqrMagnitude > 25f) // 최고 속도 제한
            rb.linearVelocity *= 0.95f;

        // 벽 접촉 마찰로 생기는 회전 누적 방지 (회전은 transform.Rotate로만 제어)
        rb.angularVelocity = Vector3.zero;
    }

    void SetAnimation()
    {
        if (animator == null) return;
        animator.speed = animationSpeed;
        Vector3 hv = rb.linearVelocity; hv.y = 0f;
        animator.SetFloat("Speed", hv.magnitude);
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("food"))
        {
            AddReward(meatReward);
            other.GetComponent<Collectible>().Respawn();
            Debug.Log("food");
        }
        else if (other.CompareTag("badFood"))
        {
            AddReward(carrotReward);
            other.GetComponent<Collectible>().Respawn();
            Debug.Log("badfood"); 
        }
    }
}

