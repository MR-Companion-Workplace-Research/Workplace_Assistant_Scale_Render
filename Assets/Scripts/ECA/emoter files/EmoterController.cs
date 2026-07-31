using UnityEngine;
using UnityEngine.Events;

public class EmoterController : MonoBehaviour
{
    [HideInInspector] public UnityEvent emoterEventNeutral;
    [HideInInspector] public UnityEvent emoterEventPositive;
    [HideInInspector] public UnityEvent emoterEventNegative;
    [HideInInspector] public UnityEvent emoterEventThinking;
    [HideInInspector] public UnityEvent waveAnim;
    [HideInInspector] public UnityEvent gestureAnim;
    [HideInInspector] public UnityEvent gesture2Anim;
    [HideInInspector] public UnityEvent headnodAnim;
    public emoteDriver emoteInvoke;
    public animDriver animatorCtrl;
    [HideInInspector] public bool isReady = false;

    // Start is called before the first frame update
    void Start()
    {
        gestureAnim.AddListener(TriggerGestureAnim);
        Debug.Log("Gesture Trigger Event Listener Added");

        gesture2Anim.AddListener(TriggerGesture2Anim);
        Debug.Log("Gesture 2 Trigger Event Listener Added");

        headnodAnim.AddListener(TriggerHeadNodAnim);
        Debug.Log("Head Nod Trigger Event Listener Added");

        waveAnim.AddListener(TriggerWaveAnim);
        Debug.Log("Wave Trigger Event Listener Added");       

        emoterEventNeutral.AddListener(TriggerEmote);
        Debug.Log("Neutral Emote Event Listener Added");

        emoterEventPositive.AddListener(PosTriggerEmote);
        Debug.Log("Positive Emote Event Listener Added"); 

        emoterEventNegative.AddListener(NegTriggerEmote);
        Debug.Log("Negative Emote Event Listener Added");

        emoterEventThinking.AddListener(ThinkTriggerEmote);
        Debug.Log("Thinking Emote Event Listener Added");

        isReady = true;
    }

    public void TriggerGestureAnim()
    {
        animatorCtrl.GestureAnim();
        Debug.Log("TriggerGestureAnim triggered");
    }

    public void TriggerGesture2Anim()
    {
        animatorCtrl.Gesture2Anim();
        Debug.Log("TriggerGesture2Anim triggered");
    }

    public void TriggerHeadNodAnim()
    {
        animatorCtrl.HeadNodAnim();
        Debug.Log("TriggerHeadNodAnim triggered");
    }

    public void TriggerWaveAnim()
    {
        animatorCtrl.WaveAnim();
        Debug.Log("TriggerWaveAnim triggered");
    }

    public void TriggerEmote()
    {
        emoteInvoke.EmoterController();
    }

    public void PosTriggerEmote()
    {
        emoteInvoke.PosEmoterController();
    }

    public void NegTriggerEmote()
    {
        emoteInvoke.NegEmoterController();
    }

    public void ThinkTriggerEmote()
    {
        emoteInvoke.ThinkEmoterController();
    }
}
