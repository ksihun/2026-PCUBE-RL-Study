using UnityEngine;

public class SoccerBallController : MonoBehaviour
{
    public GameObject area;
    [HideInInspector]
    public SoccerEnvController envController;
    public string redGoalTag; //will be used to check if collided with red goal
    public string blueGoalTag; //will be used to check if collided with blue goal

    AgentSoccer m_LastTouchAgent;
    bool m_GoalHandled;

    void Start()
    {
        envController = area.GetComponent<SoccerEnvController>();
    }

    void OnCollisionEnter(Collision col)
    {
        if (m_GoalHandled)
            return;

        if (col.gameObject.CompareTag(redGoalTag)) //ball touched red goal
        {
            m_GoalHandled = true;
            envController.GoalTouched(Team.Blue, m_LastTouchAgent);
        }
        else if (col.gameObject.CompareTag(blueGoalTag)) //ball touched blue goal
        {
            m_GoalHandled = true;
            envController.GoalTouched(Team.Red, m_LastTouchAgent);
        }
    }

    public void RecordTouch(AgentSoccer agent)
    {
        m_LastTouchAgent = agent;
    }

    public void ClearLastTouch()
    {
        m_LastTouchAgent = null;
        m_GoalHandled = false;
    }
}
