using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class EventTest : MonoBehaviour
{
    // Start is called before the first frame update
    public UnityEvent m_MyEvent;

    void Start()
    {
        if (m_MyEvent == null) {
            m_MyEvent = new UnityEvent();
        }

        m_MyEvent.AddListener(Ping);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.anyKeyDown && m_MyEvent != null) {
            m_MyEvent.Invoke();
        }
    }

    void Ping() {
        Debug.Log("Ping");
    }
}
