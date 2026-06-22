using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CrazyMinnow.SALSA;
using UnityEngine.Events;

public class animDriver : MonoBehaviour
{
    public Animator Anim;

    public void WaveAnim()
    {
        // Trigger Wave Animation
        Anim.SetTrigger("WaveTrigger");
    }

    public void GestureAnim()
    {
        // Trigger Gesture Animation
        Anim.SetTrigger("GestureTrigger");
    }

    public void HeadNodAnim()
    {
        // Trigger Head Nod Animation
        Anim.SetTrigger("HeadNodTrigger");
    }

}
