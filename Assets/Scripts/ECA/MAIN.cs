using System.Collections;
using System.Collections.Generic;
using CrazyMinnow.SALSA;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;


public class MAIN : MonoBehaviour
{
    public EmoterController emoter;
    public OpenAI_API_Connect AI_Connect;
    public AudioSource Audio;
    public TMP_Text AgentText;
    [HideInInspector] public bool PathDeleted = false;

    public void Update()
    {
        // In the MR / ElevenLabs setup there is no OpenAI text backend, so AI_Connect
        // is unassigned. Skip this driver entirely — ElevenLabsEmoteBridge drives the
        // emotes there. (In the desktop Agent_Scene, AI_Connect is assigned and this runs.)
        if (AI_Connect == null) return;

        if (emoter.isReady)
        {
            emoter.isReady = false;

            emoter.waveAnim.Invoke();
            emoter.emoterEventPositive.Invoke();
        }

        if (AI_Connect.GestureAnim)
        {
            AI_Connect.GestureAnim = false;

            emoter.gestureAnim.Invoke();
        }

        if (AI_Connect.HeadNodAnim)
        {
            AI_Connect.HeadNodAnim = false;

            emoter.headnodAnim.Invoke();
        }

        if (AI_Connect.PosAnim)
        {
            AI_Connect.PosAnim = false;

            emoter.emoterEventPositive.Invoke();
        }

        if (AI_Connect.NegAnim)
        {
            AI_Connect.NegAnim = false;

            emoter.emoterEventNegative.Invoke();
        }

        if (AI_Connect.NeuAnim)
        {
            AI_Connect.NeuAnim = false;

            emoter.emoterEventNeutral.Invoke();
        }

        if (AI_Connect.ThinkingAnim)
        {
            AI_Connect.ThinkingAnim = false;

            emoter.emoterEventThinking.Invoke();
        }

        if (AI_Connect.AgentSpeaking)
        {
            AI_Connect.AgentSpeaking = false;

            AgentText.text = " ";

            AgentText.text = AI_Connect.ParsedSpeech;
        }

        if (AI_Connect.WaveAnim)
        {
            AI_Connect.WaveAnim = false;

            emoter.waveAnim.Invoke();
        }
    }

}
