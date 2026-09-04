using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

// spawn/reset the players and ball, handle a goal (score + group reward + reset)
public class SoccerEnvController : MonoBehaviour
{
    [System.Serializable]
    public class PlayerInfo
    {
        public AgentSoccer Agent;
        [HideInInspector]
        public Vector3 StartingPos;
        [HideInInspector]
        public Quaternion StartingRot;
        [HideInInspector]
        public Rigidbody Rb;
    }

    public int MaxEnvironmentSteps = 25000;

    public bool strongRandomization = true;

    public float ballStallPenaltyScale = 0.00005f;

    public GameObject ball;
    [HideInInspector]
    public Rigidbody ballRb;
    Vector3 m_BallStartingPos;

    // List of players on this field.
    public List<PlayerInfo> AgentsList = new List<PlayerInfo>();

    public int blueScore;
    public int redScore;

    int m_ResetTimer;
    float m_PreviousBallLocalX;
    bool m_BlueHalfBonusAwarded;
    bool m_RedHalfBonusAwarded;
    SimpleMultiAgentGroup m_BlueAgentGroup;
    SimpleMultiAgentGroup m_RedAgentGroup;

    // Moving the ball from the centre to a goal contributes about 0.2 reward.
    // The reward is zero-sum, so neither side gets a systematic advantage.
    const float k_BallProgressRewardScale = 0.01f;
    const float k_OpponentHalfBonus = 0.25f;
    // These are outside the playable area (the side walls are around Z ±9.8).
    // They are an emergency escape check, not an invisible boundary inside the pitch.
    const float k_EscapeHalfX = 22f;
    const float k_EscapeHalfZ = 12f;

    void Start()
    {
        ballRb = ball.GetComponent<Rigidbody>();
        m_BallStartingPos = ball.transform.position;

        m_BlueAgentGroup = new SimpleMultiAgentGroup();
        m_RedAgentGroup = new SimpleMultiAgentGroup();

        foreach (var item in AgentsList)
        {
            item.StartingPos = item.Agent.transform.position;
            item.StartingRot = item.Agent.transform.rotation;
            item.Rb = item.Agent.GetComponent<Rigidbody>();
        }
        // Group membership is (re)built per lesson in ResetScene, so the field can shrink/grow.
        ResetScene();
    }

    void FixedUpdate()
    {
        ResetIfPlayerEscaped();
        RewardBallProgress();

        m_ResetTimer += 1;
        if (m_ResetTimer >= MaxEnvironmentSteps && MaxEnvironmentSteps > 0)
        {
            m_BlueAgentGroup.GroupEpisodeInterrupted();
            m_RedAgentGroup.GroupEpisodeInterrupted();
            ResetScene();
        }
    }

    void ResetIfPlayerEscaped()
    {
        foreach (var item in AgentsList)
        {
            if (item.Agent == null || !item.Agent.gameObject.activeInHierarchy || item.Rb == null)
                continue;

            var localPos = transform.InverseTransformPoint(item.Rb.position);
            if (Mathf.Abs(localPos.x) <= k_EscapeHalfX && Mathf.Abs(localPos.z) <= k_EscapeHalfZ)
                continue;

            // A player crossed the real wall. Reset this one field rather than
            // leaving an active but unreachable player in a neighbouring field.
            m_BlueAgentGroup.GroupEpisodeInterrupted();
            m_RedAgentGroup.GroupEpisodeInterrupted();
            ResetScene();
            return;
        }
    }

    public void ResetBall()
    {
        // Strong mode spreads the ball across most of the pitch (kept off the ±20 goal lines);
        // otherwise a small jitter around the center spot.
        var range = strongRandomization ? new Vector2(14f, 7f) : new Vector2(2.5f, 2.5f);
        var randomPosX = Random.Range(-range.x, range.x);
        var randomPosZ = Random.Range(-range.y, range.y);

        ball.transform.position = m_BallStartingPos + new Vector3(randomPosX, 0f, randomPosZ);
        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        m_PreviousBallLocalX = transform.InverseTransformPoint(ball.transform.position).x;
        m_BlueHalfBonusAwarded = m_PreviousBallLocalX > 0f;
        m_RedHalfBonusAwarded = m_PreviousBallLocalX < 0f;
        ball.GetComponent<SoccerBallController>()?.ClearLastTouch();
    }

    void RewardBallProgress()
    {
        if (ball == null || m_BlueAgentGroup == null || m_RedAgentGroup == null)
            return;

        var ballLocalX = transform.InverseTransformPoint(ball.transform.position).x;
        var previousBallLocalX = m_PreviousBallLocalX;
        var progressReward =
            (ballLocalX - previousBallLocalX) * k_BallProgressRewardScale;

        m_BlueAgentGroup.AddGroupReward(progressReward);
        m_RedAgentGroup.AddGroupReward(-progressReward);

        if (!m_BlueHalfBonusAwarded && previousBallLocalX <= 0f && ballLocalX > 0f)
        {
            m_BlueAgentGroup.AddGroupReward(k_OpponentHalfBonus);
            m_RedAgentGroup.AddGroupReward(-k_OpponentHalfBonus);
            m_BlueHalfBonusAwarded = true;
        }

        if (!m_RedHalfBonusAwarded && previousBallLocalX >= 0f && ballLocalX < 0f)
        {
            m_RedAgentGroup.AddGroupReward(k_OpponentHalfBonus);
            m_BlueAgentGroup.AddGroupReward(-k_OpponentHalfBonus);
            m_RedHalfBonusAwarded = true;
        }

        m_PreviousBallLocalX = ballLocalX;
    }

    public void GoalTouched(Team scoredTeam, AgentSoccer lastTouchAgent)
    {
        var scoredGroup = scoredTeam == Team.Blue ? m_BlueAgentGroup : m_RedAgentGroup;
        var concededGroup = scoredTeam == Team.Blue ? m_RedAgentGroup : m_BlueAgentGroup;

        scoredGroup.AddGroupReward(1f);
        concededGroup.AddGroupReward(-1f);

        var strikerOwnGoal = lastTouchAgent != null &&
            lastTouchAgent.position == AgentSoccer.Position.Striker &&
            lastTouchAgent.team != scoredTeam;

        // Mirror the goal onto each active striker's INDIVIDUAL reward.
        foreach (var item in AgentsList)
        {
            var a = item.Agent;
            if (!a.gameObject.activeSelf || a.position != AgentSoccer.Position.Striker)
                continue;
            if (a.team == scoredTeam)
                a.AddReward(1f);
            else if (strikerOwnGoal && a == lastTouchAgent)
                a.AddReward(-2f);
            else
                a.AddReward(-1f);
        }

        if (scoredTeam == Team.Blue) blueScore++; else redScore++;
        Debug.Log($"Goal! Blue {blueScore} - {redScore} Red");

        scoredGroup.EndGroupEpisode();
        concededGroup.EndGroupEpisode();
        ResetScene();
    }

    public void ResetScene()
    {
        m_ResetTimer = 0;

        // Curriculum lesson (from Python environment_parameters):
        // 0 = 2 strikers vs empty goal,
        // 1 = 2 strikers vs a lone goalie,
        // 2 = full 3v3 self-play.
        var lesson = Academy.Instance.EnvironmentParameters.GetWithDefault("lesson", 2f);

        Team randTeam = Random.value < 0.5f ? Team.Blue : Team.Red;
        // randTeam's strikers attack
        foreach (var item in AgentsList)
        {
            var agent = item.Agent;
            var active = ActiveInLesson(agent, lesson, randTeam);
            agent.gameObject.SetActive(active); // benched agents leave the field: no body, no decisions.

            var group = agent.team == Team.Blue ? m_BlueAgentGroup : m_RedAgentGroup;
            if (!active)
            {
                group.UnregisterAgent(agent);
                continue;
            }
            group.RegisterAgent(agent);

            const float pitchHalfX = 19f; // goal line begins ~20.2
            const float pitchHalfZ = 8f;  // side walls at ~9.8

            Vector3 newStartPos;
            Quaternion newRot;
            if (strongRandomization && agent.position != AgentSoccer.Position.Goalie)
            {
                // Scatter non-goalie players anywhere on the pitch, facing a random direction, so the
                // striker generalizes past the near-home kickoff layout. Goalies keep their line.
                var lx = Random.Range(-pitchHalfX, pitchHalfX);
                var lz = Random.Range(-pitchHalfZ, pitchHalfZ);
                newStartPos = new Vector3(transform.position.x + lx, agent.initialPos.y, transform.position.z + lz);
                newRot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            }
            else
            {
                // Default: small depth (x) jitter around home, clamped to the pitch, authored facing.
                var newX = agent.initialPos.x + Random.Range(-5f, 5f);
                var localX = Mathf.Clamp(newX - transform.position.x, -pitchHalfX, pitchHalfX);
                newStartPos = new Vector3(transform.position.x + localX, agent.initialPos.y, agent.initialPos.z);
                newRot = Quaternion.Euler(0, agent.rotSign * Random.Range(80f, 100f), 0);
            }
            agent.transform.SetPositionAndRotation(newStartPos, newRot);

            item.Rb.linearVelocity = Vector3.zero;
            item.Rb.angularVelocity = Vector3.zero;
        }

        //Reset Ball
        ResetBall();
    }

    // Who is on the field for a given curriculum lesson.
    static bool ActiveInLesson(AgentSoccer agent, float lesson, Team team = Team.Blue)
    {
        var striker = agent.team == team && agent.position == AgentSoccer.Position.Striker;
        if (lesson < 1f)   // Lesson 0: only the attacking strikers, empty goal.
            return striker;
        if (lesson < 2f)   // Lesson 1: strikers vs one defending goalie.
            return striker || (agent.team != team && agent.position == AgentSoccer.Position.Goalie);
        return true;       // Lesson 2: full 3v3.
    }
}
