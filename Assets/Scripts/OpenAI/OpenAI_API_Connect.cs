using UnityEngine;
using TMPro;
using OpenAI_API;
using OpenAI_API.Models;
using OpenAI_API.Chat;
using OpenAI_API.Audio;
using UnityEngine.UI;
using System.Linq;
using System.IO;
using System.Collections;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

public class OpenAI_API_Connect : MonoBehaviour
{
    private static class StatusTexts
    {
        internal const string INITIALIZING = "BMWassistant is initializing...";
        internal const string INITIALIZED = "BMWassistant is initialized";
        internal const string PROCESSING_MODERATION = "BMWassistant is processing moderation...";
        internal const string WAITING_FOR_RESPONSE = "BMWassistant is waiting for a response...";
        internal const string RESPONSE_RECEIVED = "BMWassistant received a response";
        internal const string UNKNOWN_ERROR = "An unknown error occurred";
    }
    // Using this source: https://github.com/OkGoDoIt/OpenAI-API-dotnet?tab=readme-ov-file#chat-api
    [HideInInspector] public string API_KEY = "sk-proj-ILhw8InFK1wtrbBJLUi1T3BlbkFJqBdYVgSW3z211YfS1g1o";
    [HideInInspector] public string AI_ROLE, USER_ROLE, TASK, ECA_Instruction, ToM_Instruction;
    [HideInInspector] public bool GestureAnim = false;
    [HideInInspector] public bool HeadNodAnim = false;
    [HideInInspector] public bool WaveAnim = false;
    [HideInInspector] public bool ThinkingAnim = false;
    [HideInInspector] public bool PosAnim = false;
    [HideInInspector] public bool NegAnim = false;
    [HideInInspector] public bool NeuAnim = false;
    [HideInInspector] public bool AgentSpeaking = false;
    [HideInInspector] public string ParsedSpeech;
    [SerializeField] private TMP_InputField userInput;
    [SerializeField] private TMP_Text StatusText;
    public TMP_Text TaskText;
    [SerializeField] private DemoChatBox chatBoxPrefab;
    [SerializeField] private Transform chatBoxContainer;
    [SerializeField] private ScrollRect chatScrollRect;
    public AudioSource AS;
    [HideInInspector] public string AgentResponse;
    private Conversation chat;
    private OpenAIAPI api;
    public SQLreader SR;
    private string outputPath;
    [HideInInspector] public bool isTTS;
    [HideInInspector] public bool isToM;
    [HideInInspector] public bool isCustomerService;
    [HideInInspector] public bool SignOn = false;
    private bool SayGoodbye = false;
    [HideInInspector] public bool isECA;
    public GameObject PopUp;
    [HideInInspector] public string AgentGender;
    public bool TestScene, TestToM;
    private bool isGestural = false;
    private string voiceGender;
    // Start is called before the first frame update
    void Start()
    {   
        PopUp.SetActive(false);     
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            OnSendButtonClicked();
        }

        if (SR.ReadDone)
        {
            SR.ReadDone = false;

            // Create Assistant
            StartConnection();
        }

        if (SignOn)
        {
            SignOn = false;

            PopUp.SetActive(true);
        }
    }

    public void startLevel(string level)
    {
        SceneManager.LoadScene(level);
    }

    public void StartConnection()
    {
        if (isCustomerService)
        {
            AI_ROLE = "BMW Customer Service Agent";
            USER_ROLE = "Customer";
            TASK = @"Solve a customer service issue. To complete the task, gather the following information: 
1. Vehicle Information: VIN, Model, Year, Whether Under Warranty, and Mileage.
2. Issue Details: description of the problem, when and how the issue occurred, frequency and consistency of the issue, previous attempts to resolve the issue.
3. Service History: previous service records, recurring issues, last service date and details.

If my answer is I don't know, that does not count as an answered question. If I don't understand the question, or don't know the answer to the question, help me find the answer. The task is not complete until all questions are answered.";
        }
        else
        {
            AI_ROLE = "BMW Employee Assistant";
            USER_ROLE = "BMW Employee";
            TASK = @"Complete a workplace task at BMW. To complete the task, gather the following information: 
1. User's Role and Department: User's Role, Department, and their position within the BMW team.
2. Task Details: specific details of the task at hand (i.e. objectives, deadlines, deliverables), and previous related tasks or projects for context.
3. Technical Knowledge: understanding of BMW's technical standards and practices, and familiarity with specific technologies or methodologies used in the task.

If my answer is I don't know, that does not count as an answered question. If I don't understand the question, or don't know the answer to the question, help me find the answer. The task is not complete until all questions are answered.";
        }

        if (TestScene)
        {
            if (TestToM)
            {
                ToM_Instruction = @"- Neutralizing my mental state is more important than efficiently accomplishing the task. 
You have a theory of mind. Use context clues in my responses to assume my mental state (i.e. my emotional state, intentions, and beliefs). 
Ask follow up questions with empathy and compassion, based on the context of my response. 
For instance, if I am seemingly in a frustrating or stressful situation, try to neutralize my emotions with small talk.";                
            }
            else
            {
                ToM_Instruction = string.Empty;
            }
        }
        if (!TestScene)
        {
            if (isToM)
            {
                ToM_Instruction = @"- Neutralizing my mental state is more important than efficiently accomplishing the task. 
You have a theory of mind. Use context clues in my responses to assume my mental state (i.e. my emotional state, intentions, and beliefs). 
Ask follow up questions with empathy and compassion, based on the context of my response. 
For instance, if I am seemingly in a frustrating or stressful situation, try to neutralize my emotions with small talk.";
            }
            else
            {
                ToM_Instruction = string.Empty;
            }

        }

        if (isECA)
        {
            ECA_Instruction = @"- At the end of your statement, include one of the following tags based on my previous response: <POS_RESPONSE>, <NEG_RESPONSE>, or <NEU_RESPONSE>. 
- <POS_RESPONSE> indicates that I said something positive, or indicated that I am safe, healthy, and feeling good.
- <NEG_RESPONSE> indicates that I said something negative, or indicated that I am in a harmful situation, feeling poor, or unwell. 
- <NEU_RESPONSE> indicates that I am in a neutral mood.
            ";
        }

        if (!isECA)
        {
            ECA_Instruction = string.Empty;
        }

        string Instructions = @$"
Never forget you are a {AI_ROLE} and I am a {USER_ROLE}. Never flip roles! You will always ask me questions about the task. 
We share a common interest in collaborating to successfully complete a task.
I must help you to complete the task. Here is the task: {TASK} Never forget our task!

You must ask questions based on my experience and your needs to complete the task ONLY in the following ways:
    {ToM_Instruction}
    - You should ask me questions about the task. Ask one question at a time. 
    - Ask follow-up questions based on my input to gather as much information about the task as possible. 
    - Do not repeat questions. Ask new questions every time.
    {ECA_Instruction} 

I will first give you my first name. Use my name in your responses to personalize our interaction.
Start the conversation with a polite greeting and ask how you can help me today.
I must write a response that appropriately answers the requested question. 
I must decline your question honestly if I cannot answer due to physical, moral, legal reasons or my capability and explain the reasons.  
Do not add anything else other than your question and the optional corresponding input! 
Keep asking me questions until you think the task is completed. 

When the task is completed, do the following:
1. Say goodbye and thank me for my time.
2. Include this at the end of the statement: Please inform the researcher that you have completed this module. 
            ";

        Debug.Log("Instructions: " + Instructions);        

        api = new OpenAIAPI(API_KEY);

        StatusText.text = StatusTexts.INITIALIZING;
        
        chat = api.Chat.CreateConversation();
        chat.Model = Model.GPT4_Turbo;
        chat.RequestParameters.Temperature = 0;

        // give instruction as System
        chat.AppendSystemMessage(Instructions);

        StatusText.text = StatusTexts.INITIALIZED;

        string InitialMessage = "";
        if (isCustomerService)
        {
            InitialMessage = $"Hello! My name is Sam, your personal BMW help center assistant. Before we begin, could you please tell me your name?";
        }
        else
        {
            InitialMessage = $"Hello! My name is Sam, your personal BMW workplace assistant. Before we begin, could you please tell me your name?";
        }
        
        AddAssistantChat(InitialMessage);
    }

    public async void OnSendButtonClicked()
    {
        if (isECA)
        {
            // Cue Backchanneling
            var random = new System.Random();
            var backchannel = new List<string> {"alright", "got it", "understood"}; // most common examples of backchanneling
            int index = random.Next(backchannel.Count); // randomly selects backchannel option from the list
            OpenTTS(backchannel[index]);
            isGestural = true;

            // Cue Head Nod Anim
            HeadNodAnim = true;
        }

        string userMessage = userInput.text;
        if (string.IsNullOrEmpty(userMessage)) return;

        userInput.text = string.Empty;

        // Create Input Chat
        chat.AppendUserInput(userMessage);

        // Show most recent User Response
        DemoChatBox userChatBox = AddUserChat(userMessage);
        if (userChatBox == null)
        {
            Debug.Log("User chat box is null");
            return;
        }

        // Set Status to Pending
        StatusText.text = StatusTexts.WAITING_FOR_RESPONSE;

        // and get the response
        string response = await chat.GetResponseFromChatbotAsync();
        
        // Set Status to Received
        StatusText.text = StatusTexts.RESPONSE_RECEIVED;

        // Show most recent Assistant Response
        string RecentMessage = chat.Messages.LastOrDefault().TextContent.ToString(); // Grab the lastest Message
        string RecentRole = chat.Messages.LastOrDefault().Role.ToString(); // Grab the latest Role

        await Task.Delay(1);

        if (RecentRole == "assistant")
        {
            AgentResponse = string.Empty;

            DemoChatBox addAssist = AddAssistantChat(RecentMessage);
            if (addAssist == null)
            {
                Debug.Log("Assistant chat box is null");
                return;
            }
        }

        Debug.Log($"{RecentRole}: {RecentMessage}");
    }

    private DemoChatBox AddUserChat(string message)
    {
        chat.AppendUserInput(message);

        DemoChatBox userChatBox = Instantiate(chatBoxPrefab, chatBoxContainer);
        userChatBox.SetChat(message, "User");

        ScrollToBottom();

        SR.ConversationInsert_User(message);

        return userChatBox;
    }

    private DemoChatBox AddAssistantChat(string message)
    {
        SR.ConversationInsert_Agent(message);

        DemoChatBox assistantChatBox = Instantiate(chatBoxPrefab, chatBoxContainer);
        assistantChatBox.SetChat(message, "BMWassistant");

        AgentResponse = message;
        Debug.Log("AgentResponse: " + AgentResponse);

        if (isTTS)
        {
            OpenTTS(AgentResponse);
        }

        ScrollToBottom();  

        return assistantChatBox; 
    }

    private void ScrollToBottom()
    {
        Canvas.ForceUpdateCanvases();
        chatScrollRect.verticalNormalizedPosition = 0;
    }

    public async void OpenTTS(string AgentDialogue)
    {
        if (AgentGender == "Female")
        {
            voiceGender = "nova";
        }
        else
        {
            voiceGender = "echo";
        }

        if (isECA)
        {  
           if (AgentDialogue.Contains("<POS_RESPONSE>"))
           {
                PosAnim = true;
                AgentDialogue = AgentDialogue.Replace("<POS_RESPONSE>", " ");
                Debug.Log("AgentDialogue: " + AgentDialogue);
           }

           if (AgentDialogue.Contains("<NEG_RESPONSE>"))
           {
                NegAnim = true;
                AgentDialogue = AgentDialogue.Replace("<NEG_RESPONSE>", " ");
                Debug.Log("AgentDialogue: " + AgentDialogue);
           }

           if (AgentDialogue.Contains("<NEU_RESPONSE>"))
           {
                NeuAnim = true;
                AgentDialogue = AgentDialogue.Replace("<NEU_RESPONSE>", " ");
                Debug.Log("AgentDialogue: " + AgentDialogue);
           }

           if (AgentDialogue.Contains("<TASK_COMPLETE>"))
           {
                SayGoodbye = true;
                WaveAnim = true;
                AgentDialogue = AgentDialogue.Replace("<NEU_RESPONSE>", " ");
                Debug.Log("AgentDialogue: " + AgentDialogue);
           }

            AgentSpeaking = true; // Print Agent's Speech to Subtitles

            ParsedSpeech = AgentDialogue;
            Debug.Log("ParsedSpeech: " + ParsedSpeech);
        }
        else
        {
            ParsedSpeech = AgentDialogue;
        }

        await Task.Delay(1);

        var request = new TextToSpeechRequest()
        {
            Input = ParsedSpeech,
            ResponseFormat = "mp3",
            Model = Model.TTS_HD,
            Voice = voiceGender,
            Speed = 1.0
        };

		// Save the audio file in a persistent data path
		if (Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.WindowsPlayer)
		{
			outputPath = Application.persistentDataPath + "/" + (int)(System.DateTime.UtcNow.Subtract(new System.DateTime(1970, 1, 1))).TotalSeconds + ".mp3";
		}
		else
		{
			outputPath = Application.dataPath + "/Audios/Generated/" + (int)(System.DateTime.UtcNow.Subtract(new System.DateTime(1970, 1, 1))).TotalSeconds + ".mp3";
		}

        // Create and Save Audio File
        await api.TextToSpeech.SaveSpeechToFileAsync(request, outputPath);

        // Play Audio File
        StartCoroutine(PlayAudio(outputPath));
    }

    public IEnumerator PlayAudio(string audioPath)
    {
		using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file:///" + audioPath, AudioType.MPEG))
		{
			yield return www.SendWebRequest();

			if (www.result == UnityWebRequest.Result.ConnectionError)
			{
				print(www.error);
			}
			else
			{
				AudioClip audio = DownloadHandlerAudioClip.GetContent(www);

                AS.clip = audio;
                AS.Play();

                Debug.Log("AudioSource: " + AS.name);
				
                Debug.Log("Playing Audio Clip after checking audio");
				
			}

			www.Dispose();
        }

        userInput.interactable = false; // Turns off User Input Interaction

        if (isECA)
        {
            if (!isGestural)
            {
                GestureAnim = true; // Turns on gestures
            }
            
            isGestural = false;
        }

        while (AS.isPlaying)
		{
			yield return null;
		}

        userInput.interactable = true; // Turns on User Input Interaction

        yield return new WaitForSeconds(1);

        File.Delete(audioPath);
		Debug.Log("audio_path: " + audioPath + " has deleted successfully");
		yield return null;
    }
}
