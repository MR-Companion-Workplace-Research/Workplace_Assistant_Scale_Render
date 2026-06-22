using UnityEngine;
//References
using Mono.Data.SqliteClient;
using System.Data;

public class SQLreader : MonoBehaviour
{
    [HideInInspector] public SQLreader Instance = null;
    private string conn, sqlQuery;
    IDbConnection dbconn;
    IDbCommand dbcmd;
    public string CurrentLevel;
    public GameObject MALE, FEMALE, ASSISTANT, TTS_BACKGROUND, TTS_SCROLLVIEW, ECA_Text;
    // public TMP_Text AgentTextArea, UserTextArea;
    [HideInInspector] public string pIDReaders, NameFreaders, AgentReaders, ConditionToMReaders, ConditionTaskReaders;
    [HideInInspector] public string C_IDReader, ConditionReader_01, ConditionReader_02, ConditionReader_03, ConditionReader_04, ConditionReader_05, ConditionReader_06;
    [HideInInspector] public string conditionType;
    [HideInInspector] public string UserName = "";
    [HideInInspector] public bool ReadDone = false;
    [HideInInspector] public string TaskCondition;
    private string enterNull = "NULL";
    private string filepath;
    string DatabaseName = "Participants.s3db";
    public bool TestScene;
    public bool ThreeModules;
    public OpenAI_API_Connect oAI;

    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("Unity Editor");

        filepath = Application.dataPath + "/plugins/" + DatabaseName;

        //open db connection
        conn = "URI=file:" + filepath;

        Debug.Log("Establishing connection to: " + conn);
        dbconn = new SqliteConnection(conn);
        dbconn.Open(); 

        if (!TestScene)
        {
            name_reader();
        }
        else
        {
            UserName = "Alex";
            AgentReaders = "Agent";

            ReadDone = true;
        }
        
    }

    // Insert Agent Conversation Data
    public void ConversationInsert_Agent(string text)
    {
        string newText;
        if (text.Contains(UserName)) // remove first name from local db
        {
            newText = text.Replace(UserName, string.Empty);
        }
        else
        {
            newText = text;
        }

        string AgentText = newText;
        string AgentCondition;

        if (oAI.isTTS)
        {
            AgentCondition = "ChatBot + TTS";
        }
        else if (oAI.isECA && oAI.isTTS)
        {
            AgentCondition = "ECA";
        }
        else
        {
            AgentCondition = "ChatBot";
        }

        using (dbconn = new SqliteConnection(conn))
        {
            dbconn.Open(); //Open connection to the database.
            dbcmd = dbconn.CreateCommand();

            sqlQuery = string.Format("insert into ConversationData (pID, Agent, User, Date, Time, Condition_Task, Condition_Agent) values (\"{0}\", \"{1}\", \"{2}\", date('now'), time('now'), \"{3}\", \"{4}\")", pIDReaders, AgentText, enterNull, TaskCondition, AgentCondition);// table name
            print("Executing query in " + gameObject.name);
            Debug.Log("Participants Logged...");

            dbcmd.CommandText = sqlQuery;
            dbcmd.ExecuteScalar();
            dbconn.Close();
        }
        
        Debug.Log("Insert Done...");
    }

    // Insert User Conversation Data
    public void ConversationInsert_User(string text)
    {
        string UserText = text;
        string AgentCondition;

        if (oAI.isTTS)
        {
            AgentCondition = "ChatBot + TTS";
        }
        else if (oAI.isECA && oAI.isTTS)
        {
            AgentCondition = "ECA";
        }
        else
        {
            AgentCondition = "ChatBot";
        }

        string newText;
        if (text.Contains("\""))
        {
            newText = text.Replace("\"", string.Empty);
        }
        else
        {
            newText = UserText;
        }

        using (dbconn = new SqliteConnection(conn))
        {
            dbconn.Open(); //Open connection to the database.
            dbcmd = dbconn.CreateCommand();

            sqlQuery = string.Format("insert into ConversationData (pID, Agent, User, Date, Time, Condition_Task, Condition_Agent) values (\"{0}\", \"{1}\", \"{2}\", date('now'), time('now'), \"{3}\", \"{4}\")", pIDReaders, enterNull, newText, TaskCondition, AgentCondition);// table name
            print("Executing query in " + gameObject.name);
            Debug.Log("Participants Logged...");

            dbcmd.CommandText = sqlQuery;
            dbcmd.ExecuteScalar();
            dbconn.Close();
        }
        
        Debug.Log("Insert Done...");
    }
    
    //Read All Data For To Database
    public void name_reader() // grabs information on the current user
    {
        using (dbconn = new SqliteConnection(conn))
        {
            dbconn.Open(); //Open connection to the database.
            IDbCommand dbcmd = dbconn.CreateCommand();
            string sqlQuery = "SELECT pID, NameF, Gender, Condition_ToM, Condition_Task" + " from Participants ORDER BY ROWID DESC LIMIT 1;";// table name
            dbcmd.CommandText = sqlQuery;
            IDataReader reader = dbcmd.ExecuteReader();
            while (reader.Read())
            {
                pIDReaders = reader.GetString(0);
                NameFreaders = reader.GetString(1);
                AgentReaders = reader.GetString(2);
                ConditionToMReaders = reader.GetString(3);
                ConditionTaskReaders = reader.GetString(4);
              
                Debug.Log(" pID: " + pIDReaders + "\n" + " First Name: " + NameFreaders + "\n" + " Agent Type: " + AgentReaders + "\n" + " ConditionToMReaders: " + ConditionToMReaders + "\n" + " ConditionTaskReaders: " + ConditionTaskReaders);
            
            }

        UserName = NameFreaders;
        Debug.Log("UserName: " + UserName);

        oAI.AgentGender = AgentReaders;

        reader.Close();
        reader = null;
        dbcmd.Dispose();
        dbcmd = null;
        dbconn.Close();

        }

        if (ThreeModules)
        {
            check_Condition_Task_3();
        }
        else
        {
            check_Condition_Task();
        }
        
    }

    public void check_Condition_Task_3() // Checks condition: Age of Agent
    {
        using (dbconn = new SqliteConnection(conn))
        {
            dbconn.Open(); //Open connection to the database.
            IDbCommand dbcmd = dbconn.CreateCommand();
            string sqlQuery = "SELECT C_ID, Condition_01, Condition_02, Condition_03" + " from LatinSquareThree WHERE C_ID = \"" + ConditionTaskReaders + "\";"; // grabs row that matches the selected condition
            dbcmd.CommandText = sqlQuery;
            IDataReader reader = dbcmd.ExecuteReader();
            while (reader.Read())
            {
                C_IDReader = reader.GetString(0);
                ConditionReader_01 = reader.GetString(1);
                ConditionReader_02 = reader.GetString(2);
                ConditionReader_03 = reader.GetString(3);
              
                Debug.Log(" Condition Task: " + C_IDReader + "\n" + " ConditionReader_01: " + ConditionReader_01 + "\n" + " ConditionReader_02: " + ConditionReader_02 + "\n" + " ConditionReader_03: " + ConditionReader_03);
            }
            
            reader.Close();
            reader = null;
            dbcmd.Dispose();
            dbcmd = null;
            dbconn.Close();
        
        }

        Debug.Log("CurrentLevel: " + CurrentLevel);

        // Set Level 1 Condition
        if (CurrentLevel == "1")
        {
            conditionType = ConditionReader_01;
            TaskCondition = conditionType;

            Debug.Log("conditionType: " + conditionType);

            // Define if ToM is applied
            if (conditionType == "A")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: A";
            }     

            // Define if ToM is applied
            if (conditionType == "B")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: B";
            } 

            // Define if ToM is applied
            if (conditionType == "C")
            {
                // Personal
                oAI.isCustomerService = true;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: C";
            }         
        }
        
        // Set Level 2 Condition
        if (CurrentLevel == "2")
        {
            conditionType = ConditionReader_02;
            TaskCondition = conditionType;

            Debug.Log("conditionType: " + conditionType);

            // Define if ToM is applied
            if (conditionType == "A")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: A";
            }     

            // Define if ToM is applied
            if (conditionType == "B")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: B";
            } 

            // Define if ToM is applied
            if (conditionType == "C")
            {
                // Personal
                oAI.isCustomerService = true;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: C";
            }        
        }
        
        // Set Level 3 Condition
        if (CurrentLevel == "3")
        {
            conditionType = ConditionReader_03;
            TaskCondition = conditionType;

            Debug.Log("conditionType: " + conditionType);

            // Define if ToM is applied
            if (conditionType == "A")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: A";
            }     

            // Define if ToM is applied
            if (conditionType == "B")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: B";
            } 

            // Define if ToM is applied
            if (conditionType == "C")
            {
                // Personal
                oAI.isCustomerService = true;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: C";
            }     
        }        

        if (!oAI.isECA)
        {
            ASSISTANT.gameObject.SetActive(false);
            TTS_BACKGROUND.gameObject.SetActive(true);
            TTS_SCROLLVIEW.gameObject.SetActive(true);
            ECA_Text.gameObject.SetActive(false);
        }

        if (oAI.isECA)
        {
            if (AgentReaders == "Male")
            {
                MALE.gameObject.SetActive(true);
                FEMALE.gameObject.SetActive(false);
            }    
            if (AgentReaders == "Female")
            {
                MALE.gameObject.SetActive(false);
                FEMALE.gameObject.SetActive(true);
            }

            ASSISTANT.gameObject.SetActive(true);
            TTS_BACKGROUND.gameObject.SetActive(false);
            TTS_SCROLLVIEW.gameObject.SetActive(false);
            ECA_Text.gameObject.SetActive(true);            
        }

        // Set ToM 
        if (ConditionToMReaders == "1")
        {
            oAI.isToM = true;
        }
        if (ConditionToMReaders == "2")
        {
            oAI.isToM = false;
        }

        Debug.Log("oAI.isToM: " + oAI.isToM);

        ReadDone = true;
    }

    public void check_Condition_Task() // Checks condition: Age of Agent
    {
        using (dbconn = new SqliteConnection(conn))
        {
            dbconn.Open(); //Open connection to the database.
            IDbCommand dbcmd = dbconn.CreateCommand();
            string sqlQuery = "SELECT C_ID, Condition_01, Condition_02, Condition_03, Condition_04, Condition_05, Condition_06" + " from LatinSquare WHERE C_ID = \"" + ConditionTaskReaders + "\";"; // grabs row that matches the selected condition
            dbcmd.CommandText = sqlQuery;
            IDataReader reader = dbcmd.ExecuteReader();
            while (reader.Read())
            {
                C_IDReader = reader.GetString(0);
                ConditionReader_01 = reader.GetString(1);
                ConditionReader_02 = reader.GetString(2);
                ConditionReader_03 = reader.GetString(3);
                ConditionReader_04 = reader.GetString(4);
                ConditionReader_05 = reader.GetString(5);
                ConditionReader_06 = reader.GetString(6);
              
                Debug.Log(" Condition Task: " + C_IDReader + "\n" + " ConditionReader_01: " + ConditionReader_01 + "\n" + " ConditionReader_02: " + ConditionReader_02 + "\n" + " ConditionReader_03: " + ConditionReader_03 + "\n" + " ConditionReader_04: " + ConditionReader_04 + "\n" + " ConditionReader_05: " + ConditionReader_05 + "\n" + " ConditionReader_06: " + ConditionReader_06);
            }
            
            reader.Close();
            reader = null;
            dbcmd.Dispose();
            dbcmd = null;
            dbconn.Close();
        
        }

        Debug.Log("CurrentLevel: " + CurrentLevel);

        // Set Level 1 Condition
        if (CurrentLevel == "1")
        {
            conditionType = ConditionReader_01;
            TaskCondition = conditionType;

            Debug.Log("conditionType: " + conditionType);

            // Define if ToM is applied
            if (conditionType == "A")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: A";
            }     

            // Define if ToM is applied
            if (conditionType == "B")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: B";
            } 

            // Define if ToM is applied
            if (conditionType == "C")
            {
                // Personal
                oAI.isCustomerService = true;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: C";
            } 

            // Define if ToM is applied
            if (conditionType == "D")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: D";
            }

            // Define if ToM is applied
            if (conditionType == "E")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: E";
            }

            // Define if ToM is applied
            if (conditionType == "F")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: F";
            }        
        }
        
        // Set Level 2 Condition
        if (CurrentLevel == "2")
        {
            conditionType = ConditionReader_02;
            TaskCondition = conditionType;

            Debug.Log("conditionType: " + conditionType);

            // Define if ToM is applied
            if (conditionType == "A")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: A";
            }     

            // Define if ToM is applied
            if (conditionType == "B")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: B";
            } 

            // Define if ToM is applied
            if (conditionType == "C")
            {
                // Personal
                oAI.isCustomerService = true;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: C";
            } 

            // Define if ToM is applied
            if (conditionType == "D")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: D";
            }

            // Define if ToM is applied
            if (conditionType == "E")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: E";
            }

            // Define if ToM is applied
            if (conditionType == "F")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: F";
            }         
        }
        
        // Set Level 3 Condition
        if (CurrentLevel == "3")
        {
            conditionType = ConditionReader_03;
            TaskCondition = conditionType;

            Debug.Log("conditionType: " + conditionType);

            // Define if ToM is applied
            if (conditionType == "A")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: A";
            }     

            // Define if ToM is applied
            if (conditionType == "B")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: B";
            } 

            // Define if ToM is applied
            if (conditionType == "C")
            {
                // Personal
                oAI.isCustomerService = true;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: C";
            } 

            // Define if ToM is applied
            if (conditionType == "D")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: D";
            }

            // Define if ToM is applied
            if (conditionType == "E")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: E";
            }

            // Define if ToM is applied
            if (conditionType == "F")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: F";
            }        
        }        

        // Set Level 4 Condition
        if (CurrentLevel == "4")
        {
            conditionType = ConditionReader_04;
            TaskCondition = conditionType;

            Debug.Log("conditionType: " + conditionType);

            // Define if ToM is applied
            if (conditionType == "A")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: A";
            }     

            // Define if ToM is applied
            if (conditionType == "B")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: B";
            } 

            // Define if ToM is applied
            if (conditionType == "C")
            {
                // Personal
                oAI.isCustomerService = true;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: C";
            } 

            // Define if ToM is applied
            if (conditionType == "D")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: D";
            }

            // Define if ToM is applied
            if (conditionType == "E")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: E";
            }

            // Define if ToM is applied
            if (conditionType == "F")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: F";
            }         
        }
        
        // Set Level 5 Condition
        if (CurrentLevel == "5")
        {
            conditionType = ConditionReader_05;
            TaskCondition = conditionType;

            Debug.Log("conditionType: " + conditionType);

            // Define if ToM is applied
            if (conditionType == "A")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: A";
            }     

            // Define if ToM is applied
            if (conditionType == "B")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: B";
            } 

            // Define if ToM is applied
            if (conditionType == "C")
            {
                // Personal
                oAI.isCustomerService = true;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: C";
            } 

            // Define if ToM is applied
            if (conditionType == "D")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: D";
            }

            // Define if ToM is applied
            if (conditionType == "E")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: E";
            }

            // Define if ToM is applied
            if (conditionType == "F")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: F";
            }        
        }
        
        // Set Level 6 Condition
        if (CurrentLevel == "6")
        {
            conditionType = ConditionReader_06;
            TaskCondition = conditionType;

            Debug.Log("conditionType: " + conditionType);

            // Define if ToM is applied
            if (conditionType == "A")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: A";
            }     

            // Define if ToM is applied
            if (conditionType == "B")
            {
                // Personal
                oAI.isCustomerService = true;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: B";
            } 

            // Define if ToM is applied
            if (conditionType == "C")
            {
                // Personal
                oAI.isCustomerService = true;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: C";
            } 

            // Define if ToM is applied
            if (conditionType == "D")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot
                oAI.isECA = false;
                oAI.isTTS = false;

                oAI.TaskText.text = "Task: D";
            }

            // Define if ToM is applied
            if (conditionType == "E")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ChatBot + TTS
                oAI.isECA = false;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: E";
            }

            // Define if ToM is applied
            if (conditionType == "F")
            {
                // Impersonal
                oAI.isCustomerService = false;

                // ECA
                oAI.isECA = true;
                oAI.isTTS = true;

                oAI.TaskText.text = "Task: F";
            }        
        }        

        if (!oAI.isECA)
        {
            ASSISTANT.gameObject.SetActive(false);
            TTS_BACKGROUND.gameObject.SetActive(true);
            TTS_SCROLLVIEW.gameObject.SetActive(true);
            ECA_Text.gameObject.SetActive(false);
        }

        if (oAI.isECA)
        {
            if (AgentReaders == "Male")
            {
                MALE.gameObject.SetActive(true);
                FEMALE.gameObject.SetActive(false);
            }    
            if (AgentReaders == "Female")
            {
                MALE.gameObject.SetActive(false);
                FEMALE.gameObject.SetActive(true);
            }

            ASSISTANT.gameObject.SetActive(true);
            TTS_BACKGROUND.gameObject.SetActive(false);
            TTS_SCROLLVIEW.gameObject.SetActive(false);
            ECA_Text.gameObject.SetActive(true);            
        }

        // Set ToM 
        if (ConditionToMReaders == "1")
        {
            oAI.isToM = true;
        }
        if (ConditionToMReaders == "2")
        {
            oAI.isToM = false;
        }

        Debug.Log("oAI.isToM: " + oAI.isToM);

        ReadDone = true;
    }

}
