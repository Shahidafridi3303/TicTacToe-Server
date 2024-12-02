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

    private static Dictionary<string, int[,]> gameBoards = new Dictionary<string, int[,]>(); // Room to 3x3 board
    private static Dictionary<string, int> currentTurn = new Dictionary<string, int>(); // Room to current player

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

            // Check if the room exists; if not, create it
            if (!gameRooms.ContainsKey(roomName))
                gameRooms[roomName] = new List<int>();

            // Add the player to the room
            gameRooms[roomName].Add(clientConnectionID);

            // Notify the client that they joined/created the room
            SendMessageToClient($"{ServerToClientSignifiers.GameRoomCreatedOrJoined},{roomName},{gameRooms[roomName].Count}", clientConnectionID, pipeline);

            // Check if the room is now full (2 players)
            if (gameRooms[roomName].Count == 2)
            {
                // Initialize the game logic for this room
                InitializeGame(roomName);

                // Get player connection IDs
                int player1 = gameRooms[roomName][0];
                int player2 = gameRooms[roomName][1];

                // Notify the clients of their roles and turns
                SendMessageToClient($"{ServerToClientSignifiers.StartGame},{roomName},X,1", player1, TransportPipeline.ReliableAndInOrder); // Player 1 (X)
                SendMessageToClient($"{ServerToClientSignifiers.StartGame},{roomName},O,0", player2, TransportPipeline.ReliableAndInOrder); // Player 2 (O)

                Debug.Log($"Game started in room {roomName} - Player 1: {player1} (X), Player 2: {player2} (O)");
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
        else if (signifier == ClientToServerSignifiers.PlayerMove)
        {
            string roomName = csv[1];
            int x = int.Parse(csv[2]);
            int y = int.Parse(csv[3]);

            HandlePlayerMove(roomName, clientConnectionID, x, y);
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

    // Initialize a new game board when two players join
    private static void InitializeGame(string roomName)
    {
        gameBoards[roomName] = new int[3, 3]; // Empty board
        currentTurn[roomName] = 1; // Player 1 starts
    }

    private static void HandlePlayerMove(string roomName, int clientID, int x, int y)
    {
        if (gameBoards.ContainsKey(roomName))
        {
            int[,] board = gameBoards[roomName];
            int player = currentTurn[roomName];

            // Ensure it's the correct player's turn
            if (clientID == player && board[x, y] == 0) // Empty cell and valid turn
            {
                int playerMark = (gameRooms[roomName].IndexOf(clientID) == 0) ? 1 : 2; // Player 1 = 1 (X), Player 2 = 2 (O)
                board[x, y] = playerMark;

                // Notify both players of the move
                foreach (int id in gameRooms[roomName])
                {
                    SendMessageToClient($"{ServerToClientSignifiers.PlayerMove},{x},{y},{playerMark}", id, TransportPipeline.ReliableAndInOrder);
                }

                // Check for win or draw
                if (CheckWinCondition(board, playerMark))
                {
                    foreach (int id in gameRooms[roomName])
                    {
                        SendMessageToClient($"{ServerToClientSignifiers.GameResult},{playerMark}", id, TransportPipeline.ReliableAndInOrder);
                    }
                    ResetGameRoom(roomName);
                }
                else if (CheckDrawCondition(board))
                {
                    foreach (int id in gameRooms[roomName])
                    {
                        SendMessageToClient($"{ServerToClientSignifiers.GameResult},0", id, TransportPipeline.ReliableAndInOrder);
                    }
                    ResetGameRoom(roomName);
                }
                else
                {
                    // Switch turns
                    currentTurn[roomName] = gameRooms[roomName][1 - gameRooms[roomName].IndexOf(clientID)];
                }
            }
        }
    }


    private static bool CheckWinCondition(int[,] board, int player)
    {
        // Check rows, columns, and diagonals
        for (int i = 0; i < 3; i++)
        {
            if (board[i, 0] == player && board[i, 1] == player && board[i, 2] == player) return true;
            if (board[0, i] == player && board[1, i] == player && board[2, i] == player) return true;
        }
        if (board[0, 0] == player && board[1, 1] == player && board[2, 2] == player) return true;
        if (board[0, 2] == player && board[1, 1] == player && board[2, 0] == player) return true;

        return false;
    }

    private static bool CheckDrawCondition(int[,] board)
    {
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (board[i, j] == 0) return false; // Empty cell
            }
        }
        return true;
    }

    private static void ResetGameRoom(string roomName)
    {
        gameBoards.Remove(roomName);
        currentTurn.Remove(roomName);
        gameRooms[roomName].Clear();
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

    public const int PlayerMove = 11; // Sent when a player makes a move
    public const int GameResult = 12; // Sent when the game ends
}
