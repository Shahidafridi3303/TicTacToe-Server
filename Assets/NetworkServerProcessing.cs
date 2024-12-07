using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using UnityEditor.PackageManager;

static public class NetworkServerProcessing
{
    private static Dictionary<int, string> connectionToUsername = new Dictionary<int, string>();
    private static Dictionary<string, string> accounts = new Dictionary<string, string>();
    private static string accountFilePath = "accounts.txt"; // File to save accounts
    static GameLogic gameLogic; 
    static NetworkServer networkServer;

    static public void ReceivedMessageFromClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        string[] csv = msg.Split(',');
        if (!int.TryParse(csv[0], out int signifier))
        {
            Debug.LogWarning($"Invalid signifier received: {csv[0]}");
            return;
        }

        if (signifierHandlers.ContainsKey(signifier))
        {
            signifierHandlers[signifier]?.Invoke(csv, clientConnectionID, pipeline);
        }
        else
        {
            Debug.LogWarning($"Unknown signifier received: {signifier}");
        }
    }

    private static readonly Dictionary<int, Action<string[], int, TransportPipeline>> signifierHandlers = new()
    {
        { ClientToServerSignifiers.CreateAccount, HandleCreateAccount },
        { ClientToServerSignifiers.Login, HandleLogin },
        { ClientToServerSignifiers.DeleteAccount, HandleDeleteAccount },
        { ClientToServerSignifiers.CreateOrJoinGameRoom, GameLogic.CreateOrJoinGameRoom },
        { ClientToServerSignifiers.LeaveGameRoom, GameLogic.LeaveGameRoom },
        { ClientToServerSignifiers.PlayerMove, GameLogic.PlayerMove },
        { ClientToServerSignifiers.SendMessageToOpponent, HandleSendMessageToOpponent },
        { ClientToServerSignifiers.RequestAccountList, HandleRequestAccountList },
        { ClientToServerSignifiers.ObserverJoined, GameLogic.ObserverJoined }
    };

    private static void HandleRequestAccountList(string[] csv, int clientConnectionID, TransportPipeline pipeline)
    {
        Debug.Log($"Received request for account list from Client {clientConnectionID}");

        List<string> formattedAccounts = new List<string>();
        foreach (var account in accounts)
        {
            formattedAccounts.Add($"{account.Key}:{account.Value}");
        }
        string accountList = string.Join(",", formattedAccounts);
        SendMessageToClient($"{ServerToClientSignifiers.AccountList},{accountList}", clientConnectionID, TransportPipeline.ReliableAndInOrder);
    }

    private static void HandleSendMessageToOpponent(string[] csv, int clientConnectionID, TransportPipeline pipeline)
    {
        string roomName = csv[1];
        string message = csv[2];
        var gameRooms = GameLogic.GetGameRooms();
        var observers = GameLogic.GetObservers();

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

        if (observers.ContainsKey(roomName))
        {
            foreach (int observerID in observers[roomName])
            {
                SendMessageToClient($"{ServerToClientSignifiers.OpponentMessage},{message}", observerID, pipeline);
            }
        }
    }


    private static void HandleDeleteAccount(string[] csv, int clientConnectionID, TransportPipeline pipeline)
    {
        // Validate CSV length to avoid accessing out-of-bound indices
        if (csv.Length < 3)
        {
            Debug.LogError($"Malformed DeleteAccount message received from client {clientConnectionID}. Expected 3 parts, got {csv.Length}.");
            SendMessageToClient($"{ServerToClientSignifiers.AccountDeletionFailed},InvalidRequest", clientConnectionID, pipeline);
            return;
        }

        string username = csv[1];
        string password = csv[2];

        if (accounts.TryGetValue(username, out var storedPassword) && storedPassword == password)
        {
            accounts.Remove(username);
            SaveAccounts();
            SendMessageToClient($"{ServerToClientSignifiers.AccountDeleted},{username}", clientConnectionID, pipeline);
        }
        else
        {
            SendMessageToClient($"{ServerToClientSignifiers.AccountDeletionFailed},{username}", clientConnectionID, pipeline);
        }
    }


    private static void HandleLogin(string[] csv, int clientConnectionID, TransportPipeline pipeline)
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

    private static void HandleCreateAccount(string[] csv, int clientConnectionID, TransportPipeline pipeline)
    {
        string username = csv[1];
        string password = csv[2];
        if (!accounts.ContainsKey(username))
        {
            accounts[username] = password;
            SaveAccounts();

            SendMessageToClient($"{ServerToClientSignifiers.AccountCreated}", clientConnectionID, pipeline);

            // Send updated account list
            SendUpdatedAccountListToClient(clientConnectionID);
        }

        else
        {
            SendMessageToClient($"{ServerToClientSignifiers.AccountCreationFailed}", clientConnectionID, pipeline);
        }
    }

    private static string SerializeBoardState(int[,] board)
    {
        List<string> serializedRows = new List<string>();
        for (int i = 0; i < board.GetLength(0); i++)
        {
            List<string> row = new List<string>();
            for (int j = 0; j < board.GetLength(1); j++)
            {
                row.Add(board[i, j].ToString());
            }
            serializedRows.Add(string.Join(",", row));
        }
        return string.Join(";", serializedRows);
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
        }

        var gameRooms = GameLogic.GetGameRooms();
        var observers = GameLogic.GetObservers();

        foreach (var room in gameRooms)
        {
            if (room.Value.Contains(clientConnectionID))
            {
                room.Value.Remove(clientConnectionID);

                if (room.Value.Count == 0)
                {
                    GameLogic.ClearRoomData(room.Key); // Use GameLogic method to clear room data
                }
                else
                {
                    foreach (int remainingClient in room.Value)
                    {
                        SendMessageToClient($"{ServerToClientSignifiers.GameRoomDestroyed}", remainingClient, TransportPipeline.ReliableAndInOrder);
                    }
                }
                break;
            }
        }

        foreach (var observerList in observers.Values)
        {
            if (observerList.Contains(clientConnectionID))
            {
                observerList.Remove(clientConnectionID);
                break;
            }
        }
    }

    static public void SetGameLogic(GameLogic gameLogicInstance)
    {
        gameLogic = gameLogicInstance;
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

    private static void SendUpdatedAccountListToClient(int clientConnectionID)
    {
        List<string> formattedAccounts = new List<string>();
        foreach (var account in accounts)
        {
            formattedAccounts.Add($"{account.Key}:{account.Value}");
        }
        string accountList = string.Join(",", formattedAccounts);
        SendMessageToClient($"{ServerToClientSignifiers.AccountList},{accountList}", clientConnectionID, TransportPipeline.ReliableAndInOrder);
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
    public const int PlayerMove = 11; // Ensure this exists in ClientToServerSignifiers
    public const int RequestAccountList = 13;
    public const int ObserverJoined = 14;
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
    public const int ObserverJoined = 14; // New signifier for observers joining

    public const int PlayerMove = 11; // Sent when a player makes a move
    public const int GameResult = 12; // Sent when the game ends
    public const int TurnUpdate = 13; // New signifier for turn updates

    public const int BoardStateUpdate = 15; // Sending board state to observer
    public const int GameRoomDestroyed = 16; // New signifier for destroyed rooms
}
