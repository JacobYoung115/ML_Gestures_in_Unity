using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
//https://discussions.unity.com/t/how-do-you-save-write-and-load-from-a-file/180577/2

[System.Serializable]
public class GameData {
    public int count;

    public GameData() {
        count = 0;
    }

    public GameData(int countInt) {
        count = countInt;
    }
}


public static class Save : object
{

    // Start is called before the first frame update
    public static void SaveFile(int count) {
        string destination = Application.dataPath + "/Custom/Resources/save.dat";
        FileStream file;

        if (File.Exists(destination)) file = File.OpenWrite(destination);
        else file = File.Create(destination);

        GameData data = new GameData(count);
        BinaryFormatter bf = new BinaryFormatter();
        bf.Serialize(file, data);
        file.Close();
    }

    public static GameData LoadFile() {
        string destination = Application.dataPath + "/Custom/Resources/save.dat";
        FileStream file;

        if (File.Exists(destination)) file = File.OpenRead(destination);
        else {
            Debug.Log("File not found");
            return new GameData();
        }

        BinaryFormatter bf = new BinaryFormatter();
        GameData data = (GameData) bf.Deserialize(file);
        file.Close();

        Debug.Log("Loaded in count: " + data.count);
        return data;
    }
}
