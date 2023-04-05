using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public enum GameState
{
    Init,
    RedPlayerRolls,
    RedPlayerMoves,
    WhitePlayerRolls,
    WhitePlayerMoves,
    InProgress,
    Last
}

public class GameManager : MonoBehaviourPunCallbacks
{
    public delegate void SwitchGameStateDelegate(GameState InOldState, GameState InNewState);
    public event SwitchGameStateDelegate OnStateChanged;
    public bool _isPlayerTurn = true;
    public GameObject PawnRedPrefab;
    public GameObject PawnWhitePrefab;    
    public List<FieldController> FieldsOrder = new List<FieldController>(new FieldController[26]);
    public List<DiceController> Dices = new List<DiceController>(new DiceController[2]);
    public BandController Band;
    protected GameState State = GameState.Init;
    protected PlayerColor CurrentPlayer = PlayerColor.Red;  
    protected PossibleMoves PossibleMoves = null;

    void Start()
    {
        foreach (DiceController dice in Dices)
        {
            dice.OnRolled += Dice_OnRolled;
            dice.OnUsed += Dice_OnUsed;
        }

        foreach (FieldController field in FieldsOrder)
        {
            field.OnClicked += Field_OnClicked;
        }

        StartCoroutine(StartGame(3f));

        // PhotonNetwork bağlantısını başlat
        PhotonNetwork.ConnectUsingSettings();
    }
    [PunRPC]
    public void EndTurn()
    {
        if (_isPlayerTurn)
        {
            _isPlayerTurn = false;
            // Bilgisayarın hamlesini burada yapın

            // Oyuncunun hamlesini bitirdiğine dair bilgiyi diğer oyuncuya iletin
            photonView.RPC("EndTurn", RpcTarget.Others);
        }
        else
        {
            _isPlayerTurn = true;
            // Oyuncunun hamlesini burada yapın

            // Bilgisayarın hamlesini bitirdiğine dair bilgiyi diğer oyuncuya iletin
            photonView.RPC("EndTurn", RpcTarget.Others);
        }
    }
    public override void OnConnectedToMaster()
    {
        Debug.Log("Bağlantı sağlandı.");

        // Oda oluştur veya mevcut bir odaya katıl
        PhotonNetwork.JoinOrCreateRoom("roomName", new RoomOptions(), null);
    }
    public override void OnJoinedRoom()
    {
        Debug.Log("Odaya katıldınız.");

        // Oyuncuyu oluştur ve pozisyonunu ayarla
        GameObject player = PhotonNetwork.Instantiate("PlayerPrefab", Vector3.zero, Quaternion.identity);
    }

    public void ShowPossibleMoves(FieldController InField)
    {
        Logger.Log(this, "Trying to show possible moves from field {0}.", GetIndexOf(InField));
        if (InField)
        {
            PawnController topPawn = InField.GetPawn();
            if (topPawn && CanPawnBeMoved(topPawn))
            {
                // Clearing possible moves if needed
                if (PossibleMoves != null)
                {
                    PossibleMoves.Clear();
                }

                PossibleMoves = new PossibleMoves(this, InField, Dices);
            }
        }        
    }

    public static GameManager Find()
    {
        GameObject GO = GameObject.FindGameObjectWithTag("GameManager");
        if (GO != null)
        {
            GameManager GM = GO.GetComponent<GameManager>();
            if (GM == null)
            {
                Logger.Error("GameManager", "GameManager couldn't be find on the scene. It means that it probably wasn't placed on the level in first place.");
            }

            return GM;
        }

        return null;
    }

    public GameObject GetPawnTemplate(PlayerColor InColor)
    {
        return InColor == PlayerColor.Red ? PawnRedPrefab : PawnWhitePrefab;
    }

    public FieldController GetField(int InIndex)
    {
        if (IsValidFieldIndex(InIndex))
        {
            return FieldsOrder[InIndex];
        }

        return null;
    }

    public PlayerColor GetPlayer()
    {
        return CurrentPlayer;
    }

    public GameState GetState()
    {
        return State;
    }

    public List<DiceController> GetDices()
    {
        return Dices;
    }

    public int GetIndexOf(PawnsContainer InContainer)
    {
        if (InContainer is BandController)
        {
            return CurrentPlayer == PlayerColor.Red ? -1 : 24;
        }
        else
        {
            return FieldsOrder.IndexOf(InContainer as FieldController);
        }
    }

    public bool CanPawnBeMoved(PawnController InPawn)
    {
        if (InPawn != null && CurrentPlayer == InPawn.GetColor())
        {
            return InPawn.GetColor() == PlayerColor.Red ? State == GameState.RedPlayerMoves : State == GameState.WhitePlayerMoves;
        }

        return false;
    }

    public bool IsDiceAvailable()
    {
        foreach (DiceController dice in Dices)
        {
            if(dice.GetUsageState() != DiceState.FullyUsed)
            {
                return true;
            }
        }

        return false;
    }

    protected IEnumerator StartGame(float InDelay)
    {
        yield return new WaitForSeconds(InDelay);

        Logger.Log(this, "The game has started.");
        SwitchGameState();
    }

    protected void SetState(GameState InNewState)
    {
        GameState OldState = State;
        State = InNewState;

        CurrentPlayer = (State == GameState.RedPlayerMoves || State == GameState.RedPlayerRolls) ? PlayerColor.Red : PlayerColor.White;

        Logger.Log(this, "Switching game state from {0} to {1}. It's {2} player turn.", OldState, State, CurrentPlayer);

        if (OnStateChanged != null)
        {
            OnStateChanged(OldState, State);
        }
    }

    protected void SwitchGameState()
    {
        SetState(FindNextGameState());
    }


    protected void SkipTurn()
    {
        SetState(CurrentPlayer == PlayerColor.Red ? GameState.WhitePlayerRolls : GameState.RedPlayerRolls);
    }

    protected bool MovePawnFromBand()
    {
        if(Band)
        {
            bool bKeepTrying = false;
            bool bBandHasPawns = false;
            bool bIsDiceAvailable = false;
            do
            {
                bBandHasPawns = Band.HasPawns(CurrentPlayer);
                bIsDiceAvailable = IsDiceAvailable();
                bKeepTrying = bBandHasPawns && bIsDiceAvailable;

                if (bKeepTrying)
                {
                    // Since we're here, it means that there are our pawns on Band
                    // and dices still can be used for moves.

                    // Trying to find new moves that start from Band
                    PossibleMoves = new PossibleMoves(this, Band, Dices, true);

                    // If we found any moves, we can do first one.
                    if (PossibleMoves.HasAnyMoves())
                    {
                        // Doing first available move, we've found
                        PossibleMoves.DoFirstMove();

                        // bKeepTrying is already set to true, 
                        // so we don't need to do with it anything else
                        // as we want to repeat whole process remove all
                        // our pawns from Band
                    }
                    else
                    {
                        // If we reached this place, it means that the we still 
                        // have at least one pawn placed on the Band. Since we 
                        // couldn't move it (had no possible moves) we have to skip 
                        // our turn according to backgammon rules.

                        bKeepTrying = false;
                    }
                }
            }
            while (bKeepTrying);

            bBandHasPawns = Band.HasPawns(CurrentPlayer);
            bIsDiceAvailable = IsDiceAvailable();

            if(bBandHasPawns || !bIsDiceAvailable)
            {
                return true;
            }
        }
        else
        {
            Logger.Error(this, "Couldn't move pawns from Band because reference to it is null.");
        }

        return false;   
    }
    protected virtual GameState FindNextGameState()
    {
        switch (State)
        {
            case GameState.Init:
                return GameState.RedPlayerRolls;

            case GameState.RedPlayerRolls:
                return GameState.RedPlayerMoves;

            case GameState.RedPlayerMoves:
                return GameState.WhitePlayerRolls;

            case GameState.WhitePlayerRolls:
                return GameState.WhitePlayerMoves;

            case GameState.WhitePlayerMoves:
                return GameState.RedPlayerRolls;
        }

        Logger.Error(this, "This should never happen.");
        return GameState.RedPlayerRolls;
    }
    protected bool IsValidFieldIndex(int InIndex)
    {
        return InIndex >= 0 && InIndex < FieldsOrder.Count;
    }

    protected virtual void Field_OnClicked(FieldController InField)
    {
        if (State == GameState.RedPlayerMoves || State == GameState.WhitePlayerMoves)
        {
            // If the field on which we clicked is one of possible target fields
            if (PossibleMoves != null && PossibleMoves.IsMovePossible(InField))
            {
                PossibleMoves.MoveTo(InField);
            }
        }
    }

    protected void Dice_OnUsed(DiceController InDice, DiceState InState)
    {
        // We're checking if player used all his dices already (or he has no other 
        // moves available) if so, we're skipping his turn.
        // However, we want to do that only, if the player is actually supposed to move his pawn.
        // If that's not the case and the dice was used, it means that GameManager automatically
        // moved player's pawn from Band to the board. In such scenario, we'll skip player's turn 
        // in Dice_OnRolled() after using MovePawnFromBand().

        if (State == GameState.RedPlayerMoves || State == GameState.WhitePlayerMoves)
        {
            bool bEveryFullyUsed = true;
            foreach (DiceController dice in Dices)
            {
                if (dice.GetUsageState() != DiceState.FullyUsed)
                {
                    bEveryFullyUsed = false;
                    break;
                }
            }

            // TODO: checking if there are any available moves
            bool bAvailableMovesExist = true;

            if (bEveryFullyUsed || !bAvailableMovesExist)
            {
                SwitchGameState();
            }
        }        
    }

    protected void Dice_OnRolled(DiceController InDice, int InDots)
    {
        // Checking if all dices finished rolling
        bool bFinished = true;
        foreach (DiceController dice in Dices)
        {
            if (!dice.HasFinishedRolling())
            {
                bFinished = false;
                break;
            }
        }        
        
        if (bFinished)
        {
            // We're trying to move as many pawns of current player from Band as possible.
            if(MovePawnFromBand())
            {
                // Means that there are some pawns left on the Band or player used all his dices.
                // In any of these 2 scenarios, we're skipping current player's turn.
                 
                SkipTurn();
            }
            else
            {
                SwitchGameState();
            }
        }
    }

#if UNITY_EDITOR
    protected void SetField(FieldController InField, int InIndex)
    {
        if(Application.isEditor)
        {
            if (InField && IsValidFieldIndex(InIndex))
            {
                FieldsOrder[InIndex] = InField;
            }
        }
    }
    [MenuItem("BackgammonHelpers/Setup fields order")]
    private static void SetupFieldsOrder()
    {
        var temp = Selection.activeGameObject.GetComponentsInChildren<FieldController>();
        var GM = Find();

        if (GM)
        {
            for (int iField = 0; iField < temp.Length; ++iField)
            {
                temp[iField].transform.name = string.Format("Field_{0}", iField + 1);
                GM.SetField(temp[iField], iField);
            }

            EditorUtility.SetDirty(GM);
        }
    }
    [MenuItem("BackgammonHelpers/Setup fields order", true)]
    private static bool SetupFieldsOrderValidation()
    {
        if (Selection.activeGameObject != null)
        {
            return Selection.activeGameObject.GetComponentsInChildren<FieldController>().Length > 0;
        }

        return false;
    }
#endif
}