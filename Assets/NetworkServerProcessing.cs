using System.Collections.Generic;
using UnityEngine;
using System.IO;

static public class NetworkServerProcessing
{
    private static Dictionary<int, string> connectionToUsername = new Dictionary<int, string>();
    private static Dictionary<string, string> accounts = new Dictionary<string, string>();
    private static string accountFilePath = "accounts.txt"; // File to save accounts
    static GameLogic gameLogic; // Reference to GameLogic
    static NetworkServer networkServer;

    private static Dictionary<string, List<int>> gameRooms = new Dictionary<string, List<int>>(); // Room name to list of client IDs

    public const int CreateOrJoinGameRoom = 4; // New signifier for game room creation/joining
    public const int LeaveGameRoom = 5; // New signifier for leaving a game room
    public const int StartGame = 6; // New signifier for starting the game

    static public void ReceivedMessageFromClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);

        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            string username = csv[1];
            string password = csv[2];
            if (!accounts.ContainsKey(username))
            {
                accounts[username] = password;
                SaveAccounts();
                SendMessageToClient($"{ServerToClientSignifiers.AccountCreated}", clientConnectionID, pipeline);
            }
            else
            {
                SendMessageToClient($"{ServerToClientSignifiers.AccountCreationFailed}", clientConnectionID, pipeline);
            }
        }
        else if (signifier == ClientToServerSignifiers.Login)
        {
            string username = csv[1];
            string password = csv[2];
            if (accounts.ContainsKey(username) && accounts[username] == password)
            {
                connectionToUsername[clientConnectionID] = username;
                SendMessageToClient($"{ServerToClientSignifiers.LoginSuccessful}", clientConnectionID, pipeline);
            }
            else
            {
                SendMessageToClient($"{ServerToClientSignifiers.LoginFailed}", clientConnectionID, pipeline);
            }
        }
        else if (signifier == ClientToServerSignifiers.CreateOrJoinGameRoom)
        {
            string roomName = csv[1];
            if (!gameRooms.ContainsKey(roomName))
                gameRooms[roomName] = new List<int>();

            gameRooms[roomName].Add(clientConnectionID);
            SendMessageToClient($"{ServerToClientSignifiers.GameRoomCreatedOrJoined},{roomName},{gameRooms[roomName].Count}", clientConnectionID, pipeline);

            if (gameRooms[roomName].Count == 2)
            {
                foreach (int clientID in gameRooms[roomName])
                    SendMessageToClient($"{ServerToClientSignifiers.StartGame},{roomName},Ready", clientID, pipeline);
            }
        }
        else if (signifier == ClientToServerSignifiers.LeaveGameRoom)
        {
            string roomName = csv[1];
            if (gameRooms.ContainsKey(roomName))
            {
                gameRooms[roomName].Remove(clientConnectionID);
                if (gameRooms[roomName].Count == 0)
                    gameRooms.Remove(roomName);
            }
        }
        else if (signifier == ClientToServerSignifiers.SendMessageToOpponent)
        {
            string roomName = csv[1];
            string message = csv[2];
            if (gameRooms.ContainsKey(roomName))
            {
                foreach (int clientID in gameRooms[roomName])
                {
                    if (clientID != clientConnectionID) // Send to opponent only
                    {
                        SendMessageToClient($"{ServerToClientSignifiers.OpponentMessage},{message}", clientID, pipeline);
                    }
                }
            }
        }
    }


    static public void SendMessageToClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        networkServer.SendMessageToClient(msg, clientConnectionID, pipeline);
    }

    static public void ConnectionEvent(int clientConnectionID)
    {
        LoadAccounts(); // Ensure accounts are loaded when a client connects
        List<string> formattedAccounts = new List<string>();
        foreach (var account in accounts)
        {
            formattedAccounts.Add($"{account.Key}:{account.Value}");
        }
        string accountList = string.Join(",", formattedAccounts); // Create a comma-separated list of username:password
        Debug.Log($"Sending Account List to Client {clientConnectionID}: {accountList}");
        SendMessageToClient($"{ServerToClientSignifiers.AccountList},{accountList}", clientConnectionID, TransportPipeline.ReliableAndInOrder);
    }


    static public void DisconnectionEvent(int clientConnectionID)
    {
        if (connectionToUsername.ContainsKey(clientConnectionID))
        {
            string disconnectedUsername = connectionToUsername[clientConnectionID];
            connectionToUsername.Remove(clientConnectionID);
            Debug.Log($"Client with ID {clientConnectionID} disconnected. Username: {disconnectedUsername}");
        }
        else
        {
            Debug.Log($"Client with ID {clientConnectionID} disconnected. No associated username.");
        }
    }

    static public void SetGameLogic(GameLogic GameLogic)
    {
        gameLogic = GameLogic;
    }

    private static void LoadAccounts()
    {
        if (!File.Exists(accountFilePath))
            return;

        using (StreamReader reader = new StreamReader(accountFilePath))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] accountData = line.Split(',');
                if (accountData.Length == 2)
                {
                    accounts[accountData[0]] = accountData[1]; // username, password
                }
            }
        }
        Debug.Log("Accounts loaded from file.");
    }

    private static void SaveAccounts()
    {
        using (StreamWriter writer = new StreamWriter(accountFilePath))
        {
            foreach (var account in accounts)
            {
                writer.WriteLine($"{account.Key},{account.Value}"); // username, password
            }
        }
        Debug.Log("Accounts saved to file.");
    }

    static public void SetNetworkServer(NetworkServer NetworkServer)
    {
        networkServer = NetworkServer;
    }

    static public NetworkServer GetNetworkServer()
    {
        return networkServer;
    }
}

public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int DeleteAccount = 3; // New signifier for deleting accounts

    public const int CreateOrJoinGameRoom = 4;
    public const int LeaveGameRoom = 5;
    public const int SendMessageToOpponent = 6;
}

public static class ServerToClientSignifiers
{
    public const int AccountCreated = 1;
    public const int AccountCreationFailed = 2;
    public const int LoginSuccessful = 3;
    public const int LoginFailed = 4;
    public const int AccountList = 5;
    public const int AccountDeleted = 6; // New signifier for successful deletion
    public const int AccountDeletionFailed = 7; // New signifier for failed deletion

    public const int GameRoomCreatedOrJoined = 8;
    public const int StartGame = 9;
    public const int OpponentMessage = 10;
}
