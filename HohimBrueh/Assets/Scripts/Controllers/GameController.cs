using System.Collections;
using System.Collections.Generic;
using FreeLives;
using FrogSmashers.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public enum GameState
{
    Playing,
    RoundFinished,
    JoinScreen
}


public class GameController : MonoBehaviour, ISimulated
{
    public static bool charactersBounceEachOther = false; // sim-state: exclude Round Setup (option toggle)
    public static bool weirdBounceTrajectories = false; // sim-state: exclude Round Setup (option toggle)
    public static bool onlyBounceBeforeRecover = true; // sim-state: exclude Round Setup (option toggle)
    public static bool allowTeamMode = true;

    public static List<Player> activePlayers = new List<Player>(); // sim-state: exclude the roster; saved as one membership flag per Slot, not as this list

    static List<Player> inactivePlayers; // sim-state: exclude the roster; saved as one membership flag per Slot, not as this list

    GameState state;

    public static Color overallWinnerColor = Color.red;

    public static GameState State { get { return instance.state; } }

    public Character characterPrefab;

    public Fly flyPrefab;

    float flySpawnDelay;

    public SpriteRenderer offscreenDotPrefab;

    public Canvas scoreCanvas;

    public List<PlayerScoreDisplay> playerScoreDisplays; // sim-state: exclude view

    public PlayerScoreDisplay scoreDisplayPrefab;

    static GameController instance; // sim-state: exclude singleton reference

    public Color[] playerColors; // sim-state: exclude Round Setup (colours by Slot)

    List<Color> availableColors; // sim-state: exclude join screen, before the Round

    public bool isJoinScreen;

    public static string[] levelNames = new string[] { "1BusStop", "2DownSmash", "3Moon", "4FinalFrogstination", "5Skyline", "6Finale" }; // sim-state: exclude Round Setup (the Level list)
    //public static string[] levelNames = new string[] {  "2DownSmash" };
    public JoinCanvas[] joinCanvas; // sim-state: exclude join screen view

    float finishDelay = 7.5f;

    public Text joinCountdownText, joinGameModeText; // sim-state: exclude view

    public static bool isTeamMode; // sim-state: exclude Round Setup

    public static bool playersCanDropIn; // sim-state: exclude Round Setup

    public static bool isShowDown; // sim-state: exclude Round Setup

    int redTeamScore, blueTeamScore;
    PlayerScoreDisplay redTeamScoreDisplay, blueTeamScoreDisplay; // sim-state: exclude view

    void Awake()
    {
        //Camera.main.aspect = 16f / 9f;
        if (isJoinScreen)
        {
            isShowDown = false;
            state = GameState.JoinScreen;
            finishDelay = 5f;
            SetupForJoinScreen();

            inactivePlayers = null;
            activePlayers.Clear();
            levelNo = 0;
            playersCanDropIn = true;

        }
        playerScoreDisplays = new List<PlayerScoreDisplay>();
        instance = this;

        // A round is a self-contained simulation: it starts at tick 0 when its scene loads.
        // Without this the tick kept counting across scenes, so a round's state (the fly's
        // bob phase, for example) depended on how long the previous scenes had run. Online,
        // both peers start the round together at tick 0 with the roster and seed the
        // session agreed on.
        SimClock.SeekTo(0);
        if (inactivePlayers == null)
        {
            inactivePlayers = new List<Player>();
            Player p;
            p = new Player(FreeLives.InputReader.Device.Gamepad1, playerColors[0], 0);
            inactivePlayers.Add(p);
            p = new Player(FreeLives.InputReader.Device.Gamepad2, playerColors[1], 1);
            inactivePlayers.Add(p);
            p = new Player(FreeLives.InputReader.Device.Gamepad3, playerColors[2], 2);
            inactivePlayers.Add(p);
            p = new Player(FreeLives.InputReader.Device.Gamepad4, playerColors[3], 3);
            inactivePlayers.Add(p);
            p = new Player(FreeLives.InputReader.Device.Keyboard1, playerColors[4], 4);
            inactivePlayers.Add(p);
            p = new Player(FreeLives.InputReader.Device.Keyboard2, playerColors[5], 5);
            inactivePlayers.Add(p);

        }
        else
        {
            int i = 0;
            if (isTeamMode)
            {
                foreach (var player in activePlayers)
                {
                    player.score = 0;
                    player.spawnDelay = 0.5f + 0.2f * i;
                    i++;
                }


                var psd = Instantiate(scoreDisplayPrefab, scoreCanvas.transform) as PlayerScoreDisplay;
                psd.color = Color.red;
                psd.text.color = Color.red;
                redTeamScoreDisplay = psd;
                foreach (var p in activePlayers)
                    if (p.team == Team.Red)
                        psd.player = p;

                playerScoreDisplays.Add(psd);

                psd = Instantiate(scoreDisplayPrefab, scoreCanvas.transform) as PlayerScoreDisplay;
                psd.color = Color.blue;
                psd.text.color = Color.blue;
                blueTeamScoreDisplay = psd;
                foreach (var p in activePlayers)
                    if (p.team == Team.Blue)
                        psd.player = p;
                playerScoreDisplays.Add(psd);

            }
            else
            {
                foreach (var player in activePlayers)
                {
                    player.score = 0;
                    player.spawnDelay = 0.5f + 0.2f * i;
                    i++;
                    var psd = Instantiate(scoreDisplayPrefab, scoreCanvas.transform) as PlayerScoreDisplay;
                    psd.player = player;
                    psd.color = player.color;
                    psd.text.color = player.color;
                    playerScoreDisplays.Add(psd);

                }
            }
        }

        // The Round's simulated objects: six Characters, one per Slot, and the fly. Created
        // once here and reused; nothing creates or destroys them during the Round.
        // After the Players, because each Character gets its Slot's Player.
        SimWorld.CreatePools(characterPrefab, flyPrefab);

        // Hand the Round to the driver, which seeds the random stream and picks the mode.
        // The join screen is a lobby, not a Round, so it never runs a Sync Test.
        simulation = new FrogSimulation(this);
        SimulationDriver.BeginScene(simulation, isRound: !isJoinScreen);

        if (isShowDown)
            foreach (var psd in playerScoreDisplays)
            {
                psd.gameObject.SetActive(false);
            }

        InputReader.GetInput(combinedInput);
    }

    internal static void SetupPlayersForShowdown()
    {
        List<Player> winningPlayers = GetLeadingPlayers();
        activePlayers.Clear();
        foreach (var p in winningPlayers)
            activePlayers.Add(p);
        playersCanDropIn = false;

    }

    public static JoinCanvas[] GetJoinCanvases()
    {
        return instance != null ? instance.joinCanvas : null;
    }

    public static void ToggleTeamMode()
    {
        if (!allowTeamMode || instance == null) return;
        isTeamMode = !isTeamMode;
        if (instance.joinGameModeText != null)
            instance.joinGameModeText.text = isTeamMode ? "TEAM" : "FREE  FOR  ALL";
        if (instance.joinCanvas != null)
        {
            foreach (var jc in instance.joinCanvas)
            {
                if (jc != null && jc.HasAssignedPlayer())
                    jc.RefreshForMode();
            }
        }
    }

    void Start()
    {
        float aspect = ((float)Screen.width / Screen.height);
        float screenWidth = 18f * aspect;
        float adust = 32f / screenWidth;
        Camera.main.orthographicSize = adust * 18f;
    }

    FreeLives.InputState input = new InputState(); // sim-state: exclude join-screen input, read in the frame loop before the Round
    FreeLives.InputState combinedInput = new InputState();

    // This scene's simulation, handed to the driver in Awake.
    FrogSimulation simulation; // sim-state: exclude the simulation object itself, created once per scene

    void OnDestroy()
    {
        SimulationDriver.EndScene(simulation);
        if (instance == this)
            SimWorld.Clear();
    }

    /// <summary>
    /// Match flow for one round. This is the simulation part of GameController.
    ///
    /// The boundary: a round is the simulation, from scene load to the end of the round.
    /// Everything that changes game state during a round runs here, on the tick: drop-in
    /// joins, fly spawns, respawns, and the end of the round. Scoring and the round win are
    /// in RegisterKill, which Character calls from inside its own tick.
    ///
    /// Not simulation, and so in Update(): the join screen (a lobby before the match;
    /// online, the session agrees on the roster, teams and seed before tick 0), the scene
    /// changes that FinishRound starts, and all view code (scoreboards, offscreen dots).
    ///
    /// Registered before any character exists (this is a scene object, they are spawned),
    /// so it ticks first and spawns happen at the start of a tick rather than halfway
    /// through one.
    /// </summary>
    public void SimulateTick(float deltaTime)
    {
        if (state == GameState.Playing)
        {
            if (SimWorld.Flies != null && !SimWorld.Flies.IsAlive(0) && !isShowDown)
            {
                if (flySpawnDelay > 0f)
                {
                    flySpawnDelay -= deltaTime;
                    if (flySpawnDelay <= 0f)
                        SimWorld.Flies.Spawn(0, Terrain.GetFlySpawnPoint());
                }
                else
                {
                    flySpawnDelay = SimRng.Range(15f, 45f);
                }
            }

            for (int i = 0; i < activePlayers.Count; i++)
            {
                if (activePlayers[i].character == null)
                {
                    activePlayers[i].spawnDelay -= deltaTime;
                    if (activePlayers[i].spawnDelay < 0f)
                    {
                        SpawnCharacter(activePlayers[i]);
                    }
                }
            }
        }
        else if (state == GameState.RoundFinished)
        {
            // The winner respawns for a victory lap. The other frogs keep playing, so the
            // round is still visible gameplay, and it ends on the same tick everywhere.
            for (int i = 0; i < activePlayers.Count; i++)
            {
                if (activePlayers[i].character == null && winningPlayer == activePlayers[i])
                {
                    activePlayers[i].spawnDelay -= deltaTime;
                    if (activePlayers[i].spawnDelay < 0f)
                    {
                        SpawnCharacter(activePlayers[i]);
                    }
                }
            }

            finishDelay -= deltaTime;
            if (finishDelay < 0f)
            {
                // The simulation never changes scene: a scene load cannot be rolled back. It
                // only records that the Round is over; the driver changes scene after the Tick
                // (FinishRoundAfterSimulation).
                roundOver = true;
            }
        }
    }

    // Set when the Round's end delay runs out. See SimulateTick.
    bool roundOver;

    /// <summary>True once the Round's end delay has run out.</summary>
    public bool RoundOver
    {
        get { return roundOver; }
    }

    /// <summary>
    /// Saves or loads the Round's Simulation State. Players, the roster, the
    /// Characters and the fly are synced by their own SyncState methods; FrogSimulation
    /// puts all the sections together in a fixed order.
    /// </summary>
    public void SyncState(StateStream s)
    {
        int stateValue = (int)state;
        s.Int(nameof(state), ref stateValue);
        if (s.IsReading)
            state = (GameState)stateValue;

        s.Float(nameof(flySpawnDelay), ref flySpawnDelay);
        s.Float(nameof(finishDelay), ref finishDelay);
        s.Int(nameof(redTeamScore), ref redTeamScore);
        s.Int(nameof(blueTeamScore), ref blueTeamScore);
        s.Bool(nameof(roundOver), ref roundOver);

        int winner = Player.SlotOf(winningPlayer);
        s.Slot(nameof(winningPlayer), ref winner);
        if (s.IsReading)
            winningPlayer = Player.BySlot(winner);
    }

    /// <summary>
    /// Drop-in joins, from this Tick's input. A Player who is not in the roster joins
    /// by holding X on their own device. Called by TickInput as part of step 1 of the Tick,
    /// before GameController ticks, so the new Player Spawns on the same Tick.
    ///
    /// Joins used to read the devices in Update(), from the frame loop. Now they come from
    /// the Slot's input like everything else, so a recorded or replayed Tick applies the same
    /// join on the same Tick. In online play, the roster is fixed when the Round starts.
    /// </summary>
    public static void ApplyJoins(FrogInput[] inputs)
    {
        if (instance == null || instance.state != GameState.Playing || isShowDown)
            return;

        // Slot order, so two Players joining on one Tick always join in the same order.
        for (int slot = 0; slot < inputs.Length; slot++)
        {
            var player = Player.BySlot(slot);
            if (player == null || !inputs[slot].XButton || !inactivePlayers.Contains(player))
                continue;

            // A drop-in Player keeps the colour it already has: its Slot's colour, or the one
            // it picked on the join screen. It used to be random; keeping it makes the join
            // fully deterministic.
            instance.AddPlayer(player);
            inactivePlayers.Remove(player);
        }
    }

    void Update()
    {

        

        if (state == GameState.JoinScreen)
        {

            for (int i = inactivePlayers.Count - 1; i >= 0; i--)
            {
                InputReader.GetInput(inactivePlayers[i].inputDevice, input);

                if (input.xButton)
                {
                    for (int j = 0; j < joinCanvas.Length; j++)
                    {
                        if (!joinCanvas[j].HasAssignedPlayer())
                        {
                            joinCanvas[j].AssignPlayer(inactivePlayers[i]);
                            inactivePlayers.RemoveAt(i);
                            j = joinCanvas.Length;
                        }
                    }
                }
            }


            bool playersAreReady = CheckReadyPlayers();

            if (playersAreReady)
            {
                // sim-guard: allow join-screen lobby, before the match; not part of the round's simulation
                finishDelay -= Time.deltaTime;
                joinCountdownText.text = ((int)(finishDelay) + 1).ToString();
                if (finishDelay <= 0f)
                    FinishRound();
            }
            else
            {
                joinCountdownText.text = "";
                finishDelay = 5f;
            }


        }
        else if (state == GameState.Playing)
        {
            for (int i = 0; i < activePlayers.Count; i++)
            {
                if (activePlayers[i].character != null && activePlayers[i].character.transform.position.y > Terrain.ScreenTop)
                {


                    var spr = activePlayers[i].offscreenDot;
                    if (spr == null)
                    {
                        activePlayers[i].offscreenDot = spr = Instantiate(offscreenDotPrefab);
                        spr.color = activePlayers[i].color;
                    }
                    spr.enabled = true;
                    spr.transform.position = new Vector3(activePlayers[i].character.transform.position.x, Terrain.ScreenTop, -6f);


                }
                else
                {
                    var spr = activePlayers[i].offscreenDot;
                    if (spr != null)
                    {
                        spr.enabled = false;
                    }
                }




            }

            ArrangeScoreboards();

            // Drop-in joins come from each Slot's input (ApplyJoins). The F2 key that
            // spawned a frog with no Player is gone: a Character with no Player has no Slot, so
            // it does not fit the pools.
            ReconcileScoreboard();

            // sim-guard: allow leaves the match for the title screen; not simulation
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("TitleScreen");
            }
        }
        else if (state == GameState.RoundFinished)
        {
            // The winner's respawn and the end of the round are in SimulateTick.
            ReconcileScoreboard();
            ArrangeScoreboards();
        }

        // sim-guard: allow debug key (reload the scene); not simulation
        if (Input.GetKeyDown(KeyCode.F6))
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        // sim-guard: allow debug key (toggle the settings GUI); view only
        if (Input.GetKeyDown(KeyCode.F12))
        {
            showGui = !showGui;
        }
    }

    void SetupForJoinScreen()
    {
        availableColors = new List<Color>();
        availableColors.AddRange(playerColors);

        if (joinGameModeText != null)
            joinGameModeText.text = isTeamMode ? "TEAM" : "FREE  FOR  ALL";
    }

    bool CheckReadyPlayers()
    {
        int readyPlayers = 0;
        if (GameController.isTeamMode)
        {
            bool redTeamHasPlayer = false, blueTeamHasPlayer = false;
            for (int i = 0; i < joinCanvas.Length; i++)
            {
                if (joinCanvas[i].HasAssignedPlayer())
                {
                    if (joinCanvas[i].state == JoinCanvas.State.Ready)
                    {
                        readyPlayers++;
                        if (joinCanvas[i].assignedPlayer.team == Team.Blue)
                            blueTeamHasPlayer = true;
                        else if (joinCanvas[i].assignedPlayer.team == Team.Red)
                            redTeamHasPlayer = true;
                    }
                    else
                    {
                        readyPlayers = -100;
                    }
                }
            }

            return redTeamHasPlayer && blueTeamHasPlayer && readyPlayers >= 2;
        }
        else
        {
            for (int i = 0; i < joinCanvas.Length; i++)
            {
                if (joinCanvas[i].HasAssignedPlayer())
                {
                    if (joinCanvas[i].state == JoinCanvas.State.Ready)
                        readyPlayers++;
                    else
                    {
                        readyPlayers = -100;
                    }
                }
            }
            return readyPlayers >= 2;
        }
    }

    void ArrangeScoreboards()
    {
        for (int i = 0; i < playerScoreDisplays.Count; i++)
        {
            var p = playerScoreDisplays[i].transform.localPosition;
            //print(p);
            // sim-guard: allow scoreboard animation; view only
            p.y = Mathf.Lerp(p.y, i * -2f, Time.deltaTime * 3f);
            p.z = 0f;
            //print(p);
            playerScoreDisplays[i].transform.localPosition = p;


        }
    }

    public static int levelNo; // sim-state: exclude Match total, changed between Rounds
    /// <summary>
    /// Ends the Round after the driver has run the Tick that set RoundOver: records the winner
    /// for the score screen and changes scene. Outside the simulation, so a Rollback never
    /// runs it twice.
    /// </summary>
    public static void FinishRoundAfterSimulation()
    {
        if (instance == null)
            return;
        lastWinningPlayer = instance.winningPlayer;
        instance.FinishRound();
    }

    /// <summary>
    /// The roster: one membership flag per Slot, in Slot order. On load, the roster lists are
    /// rebuilt in Slot order from the flags. Saving the flags rather than the lists keeps the
    /// Saved State the same size whoever is playing.
    /// </summary>
    public static void SyncRoster(StateStream s)
    {
        if (inactivePlayers == null)
            inactivePlayers = new List<Player>();

        bool changed = false;
        for (int slot = 0; slot < 6; slot++)
        {
            var player = Player.BySlot(slot);
            bool inRoster = player != null && activePlayers.Contains(player);
            bool before = inRoster;
            s.Bool(RosterNames[slot], ref inRoster);
            if (s.IsReading && inRoster != before)
                changed = true;
            rosterFlags[slot] = inRoster;
        }

        if (!s.IsReading || !changed)
            return;

        activePlayers.Clear();
        inactivePlayers.Clear();
        for (int slot = 0; slot < 6; slot++)
        {
            var player = Player.BySlot(slot);
            if (player == null)
                continue;
            if (rosterFlags[slot])
                activePlayers.Add(player);
            else
                inactivePlayers.Add(player);
        }
    }

    static readonly string[] RosterNames = { "inRoster[0]", "inRoster[1]", "inRoster[2]", "inRoster[3]", "inRoster[4]", "inRoster[5]" }; // sim-state: exclude field names for the Saved State, fixed
    static readonly bool[] rosterFlags = new bool[6]; // sim-state: exclude scratch space for SyncRoster

    void FinishRound()
    {
        if (state == GameState.JoinScreen)
        {
            levelNo = 0;
            foreach (var jc in joinCanvas)
            {
                if (jc.HasAssignedPlayer())
                    activePlayers.Add(jc.assignedPlayer);

            }
            SceneManager.LoadScene(levelNames[0]);
        }
        else
        {
            levelNo++;
            //if (levelNo >= 5)
            {
                SceneManager.LoadScene("ScoreScreen");
            }
        }
    }

    /// <summary>
    /// Adds a Player to the roster, kept in Slot order so the simulation always processes
    /// Players in the same order. The scoreboard follows in ReconcileScoreboard.
    /// </summary>
    void AddPlayer(Player player)
    {
        int index = 0;
        while (index < activePlayers.Count && activePlayers[index].sortPriority < player.sortPriority)
            index++;
        activePlayers.Insert(index, player);
    }

    /// <summary>
    /// Makes the per-Player scoreboard match the roster: a display for each Player in it,
    /// and none for a Player who is not. The scoreboard is view and follows
    /// Simulation State, so after a Rollback past a join it corrects itself. Free-for-all
    /// only: team mode shows two team displays instead, and Showdown hides the scoreboard.
    /// </summary>
    void ReconcileScoreboard()
    {
        if (isTeamMode || isShowDown || scoreDisplayPrefab == null)
            return;

        for (int i = playerScoreDisplays.Count - 1; i >= 0; i--)
        {
            var psd = playerScoreDisplays[i];
            if (psd == redTeamScoreDisplay || psd == blueTeamScoreDisplay)
                continue;
            if (!activePlayers.Contains(psd.player))
            {
                playerScoreDisplays.RemoveAt(i);
                Destroy(psd.gameObject);
            }
        }

        foreach (var player in activePlayers)
        {
            bool hasDisplay = false;
            foreach (var psd in playerScoreDisplays)
            {
                if (psd.player == player && psd != redTeamScoreDisplay && psd != blueTeamScoreDisplay)
                    hasDisplay = true;
            }
            if (hasDisplay)
                continue;

            var display = Instantiate(scoreDisplayPrefab, scoreCanvas.transform) as PlayerScoreDisplay;
            display.player = player;
            display.color = player.color;
            display.text.color = player.color;
            playerScoreDisplays.Add(display);
        }
    }


    void SpawnCharacter(Player player)
    {
        var point = Terrain.GetSpawnPoint();
        SimWorld.Characters.Spawn(player.sortPriority, point);
        player.spawnDelay = 1f;

        EffectsController.CreateSpawnEffects(point + Vector3.up, player.color);
        SoundController.PlaySoundEffect("CharacterSpawn", 0.4f, point);
    }

    public static void SpawnCharacterJoinScreen(Player player)
    {
        for (int i = 0; i < instance.joinCanvas.Length; i++)
        {
            if (instance.joinCanvas[i].assignedPlayer == player)
                SimWorld.Characters.Spawn(player.sortPriority, Terrain.GetSpawnPoint(i));
        }
    }

    /// <summary>Takes a Player's Character out of play, for a Player who leaves the join screen.</summary>
    public static void DespawnCharacter(Player player)
    {
        if (SimWorld.Characters != null)
            SimWorld.Characters.Despawn(player.sortPriority);
    }

    Player winningPlayer;

    public static Player lastWinningPlayer { get; private set; } // sim-state: exclude Match result for the score screen
    public static bool HasInstance { get { return instance != null; } }

    public static Player GetWinningPlayer()
    {
        if (State == GameState.RoundFinished)
        {
            return instance.winningPlayer;
        }
        return null;
    }

    void SortScoreboard()
    {
        if (GameController.isTeamMode)
        {
            redTeamScoreDisplay.player.score = redTeamScore;
            blueTeamScoreDisplay.player.score = blueTeamScore;
        }
        instance.playerScoreDisplays.Sort((x, y) => (y.player.score * 100 + y.player.sortPriority) - (x.player.score * 100 + x.player.sortPriority));
    }


    internal static void RegisterKill(Player gotPoint, Player gotKilled, int hits)
    {



        if (State == GameState.RoundFinished)
            return;

        if (isShowDown)
        {
            bool wonRound = false;
            if (activePlayers.Contains(gotKilled))
            {
                activePlayers.Remove(gotKilled);
                if (gotKilled.offscreenDot != null)
                    GameObject.Destroy(gotKilled.offscreenDot);
            }
            if (activePlayers.Count == 1)
            {
                wonRound = true;
                var winner = activePlayers[0];
                if (winner.character != null)
                    winner.character.GetComponent<ScorePlum>().ShowText("WIN!", 5f);
                instance.winningPlayer = winner;
            }
            else if (isTeamMode)
            {
                bool winnersContainRed = false;
                bool winnersContainBlue = false;
                foreach (var player in activePlayers)
                {
                    if (player.team == Team.Red)
                        winnersContainRed = true;
                    else winnersContainBlue = true;
                }

                if (!winnersContainBlue || !winnersContainRed)
                {
                    wonRound = true;
                    var winner = activePlayers[0];
                    if (winner.character != null)
                        winner.character.GetComponent<ScorePlum>().ShowText("WIN!", 5f);
                    instance.winningPlayer = winner;
                    }

            }

            if (wonRound)
            {
                SoundController.PlaySoundEffect("VictorySting", 0.5f);
                SoundController.StopMusic();
                instance.state = GameState.RoundFinished;
            }
            return;
        }

        if (gotPoint != null)
        {
            if (hits <= 0)
                hits = 1;

            if (GameController.isTeamMode)
            {
                if (gotPoint.team == Team.Blue)
                    instance.blueTeamScore += hits;
                else
                    instance.redTeamScore += hits;
            }
            else
            {
                gotPoint.score += hits;
            }

            bool wonRound = false;
            if (GameController.isTeamMode)
            {
                wonRound = ((gotPoint.team == Team.Red && instance.redTeamScore >= 10) || (gotPoint.team == Team.Blue && instance.blueTeamScore >= 10));
            }
            else
            {
                if (activePlayers.Count == 2)
                    wonRound = gotPoint.score >= 5;
                else
                    wonRound = gotPoint.score >= 10;
            }
            if (wonRound)
            {
                SoundController.PlaySoundEffect("VictorySting", 0.5f);
                SoundController.StopMusic();
                instance.state = GameState.RoundFinished;
                instance.GetPlayerScoreDisplay(gotPoint).TemorarilyDisplay("WINNER ! ! !", 5f);
                if (gotPoint.character != null)
                    gotPoint.character.GetComponent<ScorePlum>().ShowText("WIN!", 5f);
                instance.winningPlayer = gotPoint;
            }
            else
            {
                instance.GetPlayerScoreDisplay(gotPoint).TemorarilyDisplay("+" + hits.ToString());
                if (gotPoint.character != null)
                    gotPoint.character.GetComponent<ScorePlum>().ShowText("+" + hits.ToString());
            }




        }
        else if (gotKilled != null)
        {

            //if (isTeamMode)
            //{
            //    if (gotKilled.team == Team.Blue)
            //        instance.blueTeamScore--;
            //    else
            //        instance.redTeamScore--;
            //}
            //else
            //{
            //    gotKilled.score--;
            //}
            //instance.GetPlayerScoreDisplay(gotKilled).TemorarilyDisplay("-" + 1);
        }

        instance.SortScoreboard();


    }

    PlayerScoreDisplay GetPlayerScoreDisplay(Player player)
    {
        if (isTeamMode)
        {
            if (player.team == Team.Red)
                return redTeamScoreDisplay;
            else
                return blueTeamScoreDisplay;
        }
        else
        {
            for (int i = 0; i < playerScoreDisplays.Count; i++)
            {
                if (playerScoreDisplays[i].player == player)
                    return playerScoreDisplays[i];
            }
        }
        return null;
    }

    bool showGui; // sim-state: exclude view

    public void OnGUI()
    {
        // Hidden during a Sync Test: these toggles are Round Setup, and changing one in the
        // middle of a Round would make the Rollback runs differ from the first runs.
        if (showGui && !SimulationDriver.SessionActive)
        {
            GUILayout.BeginArea(new Rect(0, 0, 400, 400));
            charactersBounceEachOther = GUILayout.Toggle(charactersBounceEachOther, "Characters Bounce Each Other");
            weirdBounceTrajectories = GUILayout.Toggle(weirdBounceTrajectories, "Weird Bounce Trajectories");
            onlyBounceBeforeRecover = GUILayout.Toggle(onlyBounceBeforeRecover, "Only Bounce Before Recover");
            GUILayout.EndArea();
        }
    }

    public static Color GetAvailableColor()
    {
        // sim-guard: allow player colour on the join screen is cosmetic
        int i = UnityEngine.Random.Range(0, instance.availableColors.Count);
        var col = instance.availableColors[i];
        instance.availableColors.RemoveAt(i);
        return col;
    }

    public static void ReturnColor(Color color)
    {
        instance.availableColors.Add(color);
    }

    public static void ReturnPlayer(Player player)
    {
        activePlayers.Remove(player);
        inactivePlayers.Add(player);
    }


    public static List<Player> GetLeadingPlayers()
    {
        int topScore = -1;
        List<Player> tiedPlayers = new List<Player>();
        foreach (var player in activePlayers)
        {
            if (player.roundWins == topScore)
            {
                tiedPlayers.Add(player);
            }
            else if (player.roundWins > topScore)
            {
                topScore = player.roundWins;
                tiedPlayers.Clear();
                tiedPlayers.Add(player);
            }
        }

        return tiedPlayers;
    }

    public static bool AreAnyPlayersTiedForVictory()
    {
        int topScore = -1;
        List<Player> tiedPlayers = new List<Player>();
        foreach (var player in activePlayers)
        {
            if (player.roundWins == topScore)
            {
                tiedPlayers.Add(player);
            }
            else if (player.roundWins > topScore)
            {
                topScore = player.roundWins;
                tiedPlayers.Clear();
                tiedPlayers.Add(player);
            }
        }
        if (GameController.isTeamMode)
        {
            bool redIsTied = false, blueIsTied = false;
            foreach (var p in tiedPlayers)
            {
                if (p.team == Team.Red)
                    redIsTied = true;
                if (p.team == Team.Blue)
                    blueIsTied = true;
            }
            return redIsTied && blueIsTied;
        }
        else
            return tiedPlayers.Count > 1;

    }

}
