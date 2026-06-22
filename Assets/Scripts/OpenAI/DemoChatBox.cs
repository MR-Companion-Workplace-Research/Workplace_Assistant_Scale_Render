using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DemoChatBox : MonoBehaviour
    {
        public Text nameTxt;
        public Text chatTxt;

        public void SetChat(string userChat, string userName)
        {
            // StopTyping();
            nameTxt.text = userName;
            chatTxt.text = userChat;
        }

        public void StartTyping()
        {
            Debug.Log("Start Typing");
            chatTxt.text = "...";
            StartCoroutine(TypingRoutine());
        }

        private IEnumerator TypingRoutine()
        {
            // max 8 dots.
            string[] dots = new string[] { ".", "..", "...", "....", ".....", "......", ".......", "........" };
            int dotIndex = 0;

            while (true)
            {
                chatTxt.text = dots[dotIndex];
                dotIndex = (dotIndex + 1) % dots.Length;
                yield return new WaitForSeconds(0.5f);
            }
        }

        public void StopTyping()
        {
            Debug.Log("Stop Typing");
            StopAllCoroutines();
            chatTxt.text = string.Empty;
        }
    }