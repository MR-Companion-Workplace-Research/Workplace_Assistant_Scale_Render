using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CrazyMinnow.SALSA;
using UnityEngine.Events;

public class emoteDriver : MonoBehaviour
{
    public Emoter emoter;

    private float randomNum;

    public string[] emotePositive;

    public string[] emoteNegative;

    public string[] emoteNeutral;
    public string emoteThink;

    // Controls Neutral Expressions
    public void EmoterController()
    {
        randomNum = UnityEngine.Random.Range (0.0f, (float)emoteNeutral.Length);
        emoter.ManualEmote(emoteNeutral[(int)randomNum], ExpressionComponent.ExpressionHandler.RoundTrip, 2.0f);
        Debug.Log("TriggerEmote Function, using " + emoteNeutral[(int)randomNum]);
    }

    // Controls Positive Expressions
    public void PosEmoterController()
    {
        randomNum = UnityEngine.Random.Range (0.0f, (float)emotePositive.Length);
        emoter.ManualEmote(emotePositive[(int)randomNum], ExpressionComponent.ExpressionHandler.RoundTrip, 2.0f);
        Debug.Log("TriggerEmote Function, using " + emotePositive[(int)randomNum]);
    }

    // Controls Negative Expressions
    public void NegEmoterController()
    {
        randomNum = UnityEngine.Random.Range (0.0f, (float)emoteNegative.Length);
        emoter.ManualEmote(emoteNegative[(int)randomNum], ExpressionComponent.ExpressionHandler.RoundTrip, 2.0f);
        Debug.Log("TriggerEmote Function, using " + emoteNegative[(int)randomNum]);
    }

    // Controls Thinking Expressions
    public void ThinkEmoterController()
    {
        emoter.ManualEmote(emoteThink, ExpressionComponent.ExpressionHandler.RoundTrip, 2.0f);
        Debug.Log("TriggerEmote Function, using " + emoteThink);
    }    

}
