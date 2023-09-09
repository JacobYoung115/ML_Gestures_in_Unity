using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class EventTest2 : MonoBehaviour
{
    public UnityEvent m_External;
    public TestFunc func;

    // Start is called before the first frame update
    void Start()
    {
        m_External = new UnityEvent();
        m_External.AddListener(func.MyFunc);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) {
            m_External.Invoke();
        }
    }
}
