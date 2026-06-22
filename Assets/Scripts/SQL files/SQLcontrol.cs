using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//References
using Mono.Data.SqliteClient;
using System;
using System.Data;
using System.IO;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

public class SQLcontrol : MonoBehaviour
{
    [HideInInspector] public SQLcontrol Instance = null;
    public SceneControl SC;
    public bool DebugMode = false;
    private string conn, sqlQuery;
    IDbConnection dbconn;
    IDbCommand dbcmd;
    public TMP_InputField t_pID, t_namef;
    public TMP_Dropdown GenderType, ConditionToMType, ConditionTaskType, ModuleType;
    private string NextScene;
    private string GenderTypeValue, ConditionToMTypeValue, ConditionTaskTypeValue;
    private string filepath;
    string DatabaseName = "Participants.s3db";

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
    }

    public void GenderType_Value()
    {
        if (GenderType.value == 1)
        {
            GenderTypeValue = "Male";
            Debug.Log("Gender Type: Male");
        }

        if (GenderType.value == 2)
        {
            GenderTypeValue = "Female";
            Debug.Log("Gender Type: Female");
        }

        if (GenderType.value == 3)
        {
            // GenderTypeValue = "Non-Binary";
            Debug.Log("Gender Type: Non-Binary");
            var random = new System.Random();
            var list = new List<string>{"Male", "Female"};
            int index = random.Next(list.Count);
            var RandGender = list[index];

            GenderTypeValue = RandGender;
        }
    }

    public void ConditionToMType_Value()
    {
        if (ConditionToMType.value == 1)
        {
            ConditionToMTypeValue = "1";
            Debug.Log("ConditionToMTypeValue Type: 1");
        }

        if (ConditionToMType.value == 2)
        {
            ConditionToMTypeValue = "2";
            Debug.Log("ConditionToMTypeValue Type: 2");
        }
    }

    public void ConditionTaskType_Value()
    {
        if (ConditionTaskType.value == 1)
        {
            ConditionTaskTypeValue = "1";
            Debug.Log("ConditionTaskTypeValue Type: 1");
        }

        if (ConditionTaskType.value == 2)
        {
            ConditionTaskTypeValue = "2";
            Debug.Log("ConditionTaskTypeValue Type: 2");
        }

        if (ConditionTaskType.value == 3)
        {
            ConditionTaskTypeValue = "3";
            Debug.Log("ConditionTaskTypeValue Type: 3");
        }
        
        if (ConditionTaskType.value == 4)
        {
            ConditionTaskTypeValue = "4";
            Debug.Log("ConditionTaskTypeValue Type: 4");
        }
        
        if (ConditionTaskType.value == 5)
        {
            ConditionTaskTypeValue = "5";
            Debug.Log("ConditionTaskTypeValue Type: 5");
        }
        
        if (ConditionTaskType.value == 6)
        {
            ConditionTaskTypeValue = "6";
            Debug.Log("ConditionTaskTypeValue Type: 6");
        }
    }

    public void insert_button()
    {
        insert_function(t_pID.text, t_namef.text);
    }

    public void insert_function(string pID, string namef)
    {
        using (dbconn = new SqliteConnection(conn))
        {
            dbconn.Open(); //Open connection to the database.
            dbcmd = dbconn.CreateCommand();
            
            sqlQuery = string.Format("insert into Participants (pID, Namef, Gender, Condition_ToM, Condition_Task) values (\"{0}\", \"{1}\", \"{2}\", \"{3}\", \"{4}\")", pID, namef, GenderTypeValue, ConditionToMTypeValue, ConditionTaskTypeValue);// table name
            
            Debug.Log("pID: " + pID + ", Namef: " + namef + ", Gender: " + GenderTypeValue + ", ConditionToMType: " + ConditionToMTypeValue + ", ConditionTaskType: " + ConditionTaskTypeValue);

            Debug.Log("sqlQuery: " + sqlQuery);

            print("Executing query in" + gameObject.name);
            Debug.Log("Logged...");

            dbcmd.CommandText = sqlQuery;
            dbcmd.ExecuteScalar();
            dbconn.Close();
        }
        
        Debug.Log("Insert Done...");
    }
}
